using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TorBoxRefresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TorBoxRefresh.Tests;

/// <summary>
/// Exercises the scan -> TMC control API path without a real Jellyfin server.
/// The service reads Plugin.Instance normally, so these tests use the
/// configuration-accessor constructor added as a test seam.
/// </summary>
public sealed class TorBoxRefreshServiceTests
{
    private const string BaseUrl = "http://tmc.test";
    private const string RefreshUrl = BaseUrl + "/refresh";

    [Fact]
    public async Task Health404_UsesLegacySynchronousRefresh()
    {
        var handler = new ScriptedHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Empty(HttpStatusCode.NotFound),
            "/refresh" => Json(
                HttpStatusCode.OK,
                new
                {
                    refreshed = true,
                    added = 2,
                    removed = 1,
                    updated = 3,
                    changed = 6,
                    partial = false,
                    mode = "strm",
                }),
            _ => Empty(HttpStatusCode.NotFound),
        });

        var result = await CreateService(handler).RefreshAsync(CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.Equal(3, result.Updated);
        Assert.Equal(6, result.Changed);
        Assert.Equal("strm", result.Mode);
        Assert.Equal(1, handler.Count("/health"));
        Assert.Equal(1, handler.Count("/refresh"));
        Assert.Equal(0, handler.Count("/refresh/jobs"));

        var refresh = Assert.Single(handler.Requests, item => item.Path == "/refresh");
        Assert.Equal(HttpMethod.Post, refresh.Method);
        Assert.Equal("Bearer secret", refresh.Authorization);
        Assert.Contains("\"force\":false", refresh.Body);
    }

    [Fact]
    public async Task ApiVersion2_CreatesJobPollsUntilCompletedAndReturnsCounts()
    {
        var handler = new ScriptedHandler((request, index) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/health":
                    return Json(HttpStatusCode.OK, new { api_version = 2 });
                case "/refresh/jobs" when request.Method == HttpMethod.Post:
                    return Json(
                        HttpStatusCode.Accepted,
                        new
                        {
                            job_id = "job-1",
                            status = "queued",
                            status_url = "/refresh/jobs/job-1",
                        });
                case "/refresh/jobs/job-1":
                    return index == 0
                        ? Json(HttpStatusCode.OK, new { job_id = "job-1", status = "running" })
                        : Json(
                            HttpStatusCode.OK,
                            new
                            {
                                job_id = "job-1",
                                status = "completed",
                                result = new
                                {
                                    refreshed = true,
                                    added = 4,
                                    removed = 0,
                                    updated = 1,
                                    changed = 5,
                                    partial = false,
                                    mode = "fuse",
                                },
                            });
                default:
                    return Empty(HttpStatusCode.NotFound);
            }
        });

        var result = await CreateService(handler).RefreshAsync(CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(4, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(5, result.Changed);
        Assert.Equal("fuse", result.Mode);
        Assert.Equal(1, handler.Count("/refresh/jobs"));
        Assert.Equal(2, handler.Count("/refresh/jobs/job-1"));
    }

    [Fact]
    public async Task FailedJob_ReturnsNoResult()
    {
        var handler = new ScriptedHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/health" => Json(HttpStatusCode.OK, new { api_version = 2 }),
            "/refresh/jobs" => Json(
                HttpStatusCode.Accepted,
                new { job_id = "job-failed", status = "queued", status_url = "/refresh/jobs/job-failed" }),
            "/refresh/jobs/job-failed" => Json(
                HttpStatusCode.OK,
                new { job_id = "job-failed", status = "failed", error = "torbox down" }),
            _ => Empty(HttpStatusCode.NotFound),
        });

        var result = await CreateService(handler).RefreshAsync(CancellationToken.None);

        Assert.False(result.Refreshed);
        Assert.Equal(0, result.Changed);
        Assert.Null(result.Mode);
        Assert.Equal(1, handler.Count("/refresh/jobs/job-failed"));
    }

    [Theory]
    [InlineData(false, 409)]
    [InlineData(false, 429)]
    [InlineData(true, 409)]
    [InlineData(true, 429)]
    public async Task AlreadyHandledStatuses_ReturnNoResult(bool versionedHealth, int statusCode)
    {
        var status = (HttpStatusCode)statusCode;
        var handler = new ScriptedHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/health" => versionedHealth
                ? Json(HttpStatusCode.OK, new { api_version = 2 })
                : Empty(HttpStatusCode.NotFound),
            "/refresh" => Json(status, new { error = "busy" }),
            "/refresh/jobs" => Json(status, new { error = "busy" }),
            _ => Empty(HttpStatusCode.NotFound),
        });

        var result = await CreateService(handler).RefreshAsync(CancellationToken.None);

        Assert.False(result.Refreshed);
        Assert.Equal(0, result.Changed);
        Assert.Null(result.Mode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0, handler.Count("/refresh/jobs/job-1"));
    }

    [Fact]
    public async Task TransientServerError_IsRetried()
    {
        var handler = new ScriptedHandler((request, index) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/health":
                    return index == 0
                        ? Empty(HttpStatusCode.BadGateway)
                        : Json(HttpStatusCode.OK, new { api_version = 2 });
                case "/refresh/jobs":
                    return Json(
                        HttpStatusCode.Accepted,
                        new { job_id = "job-retry", status = "queued", status_url = "/refresh/jobs/job-retry" });
                case "/refresh/jobs/job-retry":
                    return Json(
                        HttpStatusCode.OK,
                        new
                        {
                            job_id = "job-retry",
                            status = "completed",
                            result = new
                            {
                                refreshed = true,
                                added = 1,
                                removed = 0,
                                updated = 0,
                                changed = 1,
                                partial = false,
                                mode = "strm",
                            },
                        });
                default:
                    return Empty(HttpStatusCode.NotFound);
            }
        });

        var result = await CreateService(handler).RefreshAsync(CancellationToken.None);

        Assert.True(result.Refreshed);
        Assert.Equal(1, result.Changed);
        Assert.Equal(2, handler.Count("/health"));
        Assert.Equal(1, handler.Count("/refresh/jobs"));
    }

    [Fact]
    public void ServiceCollection_ResolvesProductionConstructor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(new ScriptedHandler(
            (_, _) => Empty(HttpStatusCode.NotFound))));
        services.AddSingleton<ILogger<TorBoxRefreshService>>(
            NullLogger<TorBoxRefreshService>.Instance);
        services.AddSingleton<TorBoxRefreshService>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<TorBoxRefreshService>(
            provider.GetRequiredService<TorBoxRefreshService>());
    }

    private static TorBoxRefreshService CreateService(ScriptedHandler handler)
    {
        var config = new PluginConfiguration
        {
            Enabled = true,
            RefreshUrl = RefreshUrl,
            Token = "secret",
            TimeoutSeconds = 10,
            CooldownSeconds = 0,
        };

        return new TorBoxRefreshService(
            new FakeHttpClientFactory(handler),
            NullLogger<TorBoxRefreshService>.Instance,
            () => config);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };
    }

    private static HttpResponseMessage Empty(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<CapturedRequest> Requests { get; } = new();

        public int Count(string path)
        {
            lock (_gate)
            {
                return _counts.TryGetValue(path, out var count) ? count : 0;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            int index;
            lock (_gate)
            {
                Requests.Add(new CapturedRequest(
                    request.Method,
                    path,
                    request.Headers.Authorization?.ToString(),
                    body));
                index = _counts.TryGetValue(path, out var count) ? count : 0;
                _counts[path] = index + 1;
            }

            return _respond(request, index);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        string? Body);
}
