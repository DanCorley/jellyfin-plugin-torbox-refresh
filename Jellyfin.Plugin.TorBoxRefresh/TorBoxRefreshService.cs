using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorBoxRefresh;

public sealed class TorBoxRefreshService
{
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

            using var request = new HttpRequestMessage(HttpMethod.Post, config.RefreshUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
            request.Content = JsonContent.Create(new { force = false });

            using var response = await http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests)
            {
                _logger.LogDebug("TorBox refresh skipped: {StatusCode}", response.StatusCode);
                return RefreshResult.None;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<RefreshResult>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result ?? RefreshResult.None;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
