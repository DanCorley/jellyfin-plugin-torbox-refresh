using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorBoxRefresh;

public sealed class TorBoxRefreshService
{
    /// <summary>
    /// How often the async TMC job is polled. Fixed at 2 seconds per the control
    /// API contract; no plugin setting is exposed so the XML config schema stays stable.
    /// </summary>
    private static readonly TimeSpan JobPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Bounded retry policy for transient failures only: HTTP 5xx and request timeouts.
    /// 401, 409 and 429 are never retried.
    /// </summary>
    private const int MaxAttempts = 3;

    private const string RefreshSuffix = "/refresh";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TorBoxRefreshService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _lastAttemptUtc = DateTime.MinValue;

    public TorBoxRefreshService(
        IHttpClientFactory httpClientFactory,
        ILogger<TorBoxRefreshService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null
            || !config.Enabled
            || string.IsNullOrWhiteSpace(config.RefreshUrl)
            || string.IsNullOrWhiteSpace(config.Token))
        {
            return RefreshResult.None;
        }

        var cooldown = TimeSpan.FromSeconds(Math.Max(0, config.CooldownSeconds));
        if (DateTime.UtcNow - _lastAttemptUtc < cooldown)
        {
            return RefreshResult.None;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - _lastAttemptUtc < cooldown)
            {
                return RefreshResult.None;
            }

            _lastAttemptUtc = DateTime.UtcNow;

            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds));

            var baseUrl = GetBaseUrl(config.RefreshUrl);
            var apiVersion = await GetApiVersionAsync(http, baseUrl, config.Token, cancellationToken)
                .ConfigureAwait(false);

            if (apiVersion >= 2)
            {
                return await RefreshWithJobAsync(http, baseUrl, config, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await RefreshLegacyAsync(http, baseUrl, config, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "TorBox refresh timed out after {TimeoutSeconds}s",
                config.TimeoutSeconds);
            return RefreshResult.None;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TorBox refresh request failed");
            return RefreshResult.None;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns the TMC control API version from <c>GET /health</c>, or <c>null</c> when
    /// the server has no versioned health endpoint and the legacy synchronous
    /// <c>POST /refresh</c> should be used. Authentication failures are not a fallback.
    /// </summary>
    private async Task<int?> GetApiVersionAsync(
        HttpClient http,
        string baseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        var healthUrl = baseUrl + "/health";

        using var response = await SendWithRetryAsync(
            http,
            () => CreateRequest(HttpMethod.Get, healthUrl, token),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "TMC {HealthUrl} returned 404; using the legacy synchronous refresh endpoint",
                healthUrl);
            return null;
        }

        // A 401 or any other error is a real failure, never a fallback.
        response.EnsureSuccessStatusCode();

        try
        {
            var health = await response.Content
                .ReadFromJsonAsync<HealthResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return health?.ApiVersion;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "TMC {HealthUrl} returned an unreadable health payload; using the legacy synchronous refresh endpoint",
                healthUrl);
            return null;
        }
    }

    private async Task<RefreshResult> RefreshLegacyAsync(
        HttpClient http,
        string baseUrl,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var refreshUrl = baseUrl + RefreshSuffix;

        using var response = await SendWithRetryAsync(
            http,
            () => CreateRequest(
                HttpMethod.Post,
                refreshUrl,
                config.Token,
                JsonContent.Create(new { force = false })),
            cancellationToken).ConfigureAwait(false);

        if (IsAlreadyHandled(response.StatusCode))
        {
            _logger.LogInformation(
                "TorBox refresh skipped: TMC returned {StatusCode} (already running or cooling down)",
                response.StatusCode);
            return RefreshResult.None;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<RefreshResult>(cancellationToken: cancellationToken)
            .ConfigureAwait(false) ?? RefreshResult.None;

        LogRefreshCompleted("legacy-sync", result);
        return result;
    }

    private async Task<RefreshResult> RefreshWithJobAsync(
        HttpClient http,
        string baseUrl,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var jobsUrl = baseUrl + "/refresh/jobs";

        RefreshJobHandle? job;
        using (var response = await SendWithRetryAsync(
            http,
            () => CreateRequest(
                HttpMethod.Post,
                jobsUrl,
                config.Token,
                JsonContent.Create(new { force = false })),
            cancellationToken).ConfigureAwait(false))
        {
            if (IsAlreadyHandled(response.StatusCode))
            {
                _logger.LogInformation(
                    "TorBox refresh skipped: TMC returned {StatusCode} (already running or cooling down)",
                    response.StatusCode);
                return RefreshResult.None;
            }

            response.EnsureSuccessStatusCode();

            job = await response.Content
                .ReadFromJsonAsync<RefreshJobHandle>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(job?.JobId))
        {
            _logger.LogWarning("TMC accepted an async refresh job but did not return a job_id");
            return RefreshResult.None;
        }

        var jobId = job.JobId;
        var statusUrl = ResolveStatusUrl(baseUrl, jobId, job.StatusUrl);
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, config.TimeoutSeconds));
        using var pollTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    _logger.LogWarning(
                        "TorBox refresh job {JobId} did not finish within {TimeoutSeconds}s",
                        jobId,
                        config.TimeoutSeconds);
                    return RefreshResult.None;
                }

                pollTokenSource.CancelAfter(remaining);

                using var response = await SendWithRetryAsync(
                    http,
                    () => CreateRequest(HttpMethod.Get, statusUrl, config.Token),
                    pollTokenSource.Token).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var status = await response.Content
                    .ReadFromJsonAsync<RefreshJobStatus>(cancellationToken: pollTokenSource.Token)
                    .ConfigureAwait(false) ?? new RefreshJobStatus();

                switch (status.Status?.ToLowerInvariant())
                {
                    case "completed":
                        var result = status.Result ?? RefreshResult.None;
                        LogRefreshCompleted(jobId, result);
                        return result;

                    case "failed":
                        _logger.LogWarning(
                            "TorBox refresh job {JobId} failed: {Error}",
                            jobId,
                            status.Error ?? "unknown error");
                        return RefreshResult.None;

                    case "queued":
                    case "running":
                        break;

                    default:
                        _logger.LogDebug(
                            "TorBox refresh job {JobId} returned unknown status {Status}",
                            jobId,
                            status.Status);
                        break;
                }

                await Task.Delay(JobPollInterval, pollTokenSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "TorBox refresh job {JobId} did not finish within {TimeoutSeconds}s",
                jobId,
                config.TimeoutSeconds);
            return RefreshResult.None;
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient http,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = requestFactory();

                var response = await http
                    .SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
                {
                    _logger.LogDebug(
                        "TorBox request returned {StatusCode}; retrying (attempt {Attempt}/{MaxAttempts})",
                        response.StatusCode,
                        attempt,
                        MaxAttempts);
                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                _logger.LogDebug(
                    ex,
                    "TorBox request timed out; retrying (attempt {Attempt}/{MaxAttempts})",
                    attempt,
                    MaxAttempts);
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException("TorBox request failed after all retry attempts.");
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        return Task.Delay(delay, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string token,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content is not null)
        {
            request.Content = content;
        }

        return request;
    }

    private static string GetBaseUrl(string refreshUrl)
    {
        var url = refreshUrl.Trim().TrimEnd('/');
        if (url.EndsWith(RefreshSuffix, StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^RefreshSuffix.Length].TrimEnd('/');
        }

        return url;
    }

    private static string ResolveStatusUrl(string baseUrl, string jobId, string? statusUrl)
    {
        if (!string.IsNullOrWhiteSpace(statusUrl)
            && Uri.TryCreate(statusUrl, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        return $"{baseUrl}/refresh/jobs/{Uri.EscapeDataString(jobId)}";
    }

    private static bool IsAlreadyHandled(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests;
    }

    private void LogRefreshCompleted(string jobId, RefreshResult result)
    {
        _logger.LogInformation(
            "TorBox refresh completed: job={JobId}, changed={Changed}, added={Added}, removed={Removed}, updated={Updated}, partial={Partial}",
            jobId,
            result.Changed,
            result.Added,
            result.Removed,
            result.Updated,
            result.Partial);
    }
}

public sealed class RefreshResult
{
    public static RefreshResult None => new();

    [JsonPropertyName("refreshed")]
    public bool Refreshed { get; set; }

    [JsonPropertyName("added")]
    public int Added { get; set; }

    [JsonPropertyName("removed")]
    public int Removed { get; set; }

    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    [JsonPropertyName("changed")]
    public int Changed { get; set; }

    [JsonPropertyName("partial")]
    public bool Partial { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

public sealed class HealthResponse
{
    [JsonPropertyName("api_version")]
    public int? ApiVersion { get; set; }
}

public sealed class RefreshJobHandle
{
    [JsonPropertyName("job_id")]
    public string? JobId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_url")]
    public string? StatusUrl { get; set; }
}

public sealed class RefreshJobStatus
{
    [JsonPropertyName("job_id")]
    public string? JobId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    // TMC returns Unix epoch seconds, matching /health last_finished.
    [JsonPropertyName("started_at")]
    public double? StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public double? FinishedAt { get; set; }

    [JsonPropertyName("result")]
    public RefreshResult? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
