using System.Net;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Infrastructure.Callbacks;
using DTMS.SharedKernel.Caching;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// Phase S.5 (B2) — the federated dispatcher must honor a per-row route override
// (CallbackPath/CallbackMethod) so OMS keeps its existing REST paths, while
// defaulting to POST /events for every existing subscriber (delivered/cancelled).
public class HttpSourceCallbackDispatcherTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _scripted = new();
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? Last => Requests.Count > 0 ? Requests[^1] : null;

        /// <summary>Queue responses; when empty, answers 200 OK.</summary>
        public void Script(params HttpStatusCode[] statuses)
        {
            foreach (var s in statuses) _scripted.Enqueue(s);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var status = _scripted.Count > 0 ? _scripted.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    private static (HttpSourceCallbackDispatcher dispatcher, CapturingHandler handler,
        DTMS.Iam.Application.Callbacks.ICallbackTokenRefresher refresher) Build(
        string baseUrl, params CachedCredential[] creds)
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);

        var cache = Substitute.For<ITieredCache>();
        var credSeq = creds.Length > 0
            ? creds
            : new[]
            {
                new CachedCredential
                {
                    SystemKey = "oms",
                    CallbackBaseUrl = baseUrl,
                    CallbackTimeoutMs = 5000,   // non-zero — 0 would time out immediately
                    // CallbackAuthScheme null → no auth header applied
                },
            };
        cache.GetAsync<CachedCredential>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(credSeq[0], credSeq[1..]);
        var reader = new CachedCredentialReader(
            cache, Substitute.For<ISystemCredentialRepository>(),
            new DTMS.Iam.Infrastructure.Security.CallbackTokenProtector(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));

        var refresher = Substitute.For<DTMS.Iam.Application.Callbacks.ICallbackTokenRefresher>();
        refresher.RefreshAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(DTMS.Iam.Application.Callbacks.RefreshResult.Skipped("not configured"));

        var dispatcher = new HttpSourceCallbackDispatcher(
            http, reader, refresher, NullLogger<HttpSourceCallbackDispatcher>.Instance);
        return (dispatcher, handler, refresher);
    }

    private static CachedCredential BearerCred(string baseUrl, string token) => new()
    {
        SystemKey = "oms",
        CallbackBaseUrl = baseUrl,
        CallbackTimeoutMs = 5000,
        CallbackAuthScheme = "bearer",
        CallbackAuthConfig = $"{{\"token\":\"{token}\"}}",
    };

    private static OutboxMessage Row(string? callbackPath = null, string? callbackMethod = null) =>
        new(Guid.NewGuid(), "shipment.started.v1", "{}", DateTime.UtcNow,
            partitionKey: "oms", callbackPath: callbackPath, callbackMethod: callbackMethod);

    [Fact]
    public async Task Dispatch_DefaultsToEventsPost_WhenRouteNull()
    {
        var (dispatcher, handler, _) = Build("http://oms.local:5002/");

        await dispatcher.DispatchAsync("oms", Row(), CancellationToken.None);

        handler.Last!.RequestUri!.ToString().Should().Be("http://oms.local:5002/events");
        handler.Last.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task Dispatch_UsesCallbackPath_WhenSet()
    {
        var (dispatcher, handler, _) = Build("http://oms.local:5002/");   // trailing slash trimmed

        await dispatcher.DispatchAsync("oms", Row(callbackPath: "/api/shipments"), CancellationToken.None);

        handler.Last!.RequestUri!.ToString().Should().Be("http://oms.local:5002/api/shipments");
    }

    [Fact]
    public async Task Dispatch_NormalizesLeadingSlash_AndHonorsMethodOverride()
    {
        var (dispatcher, handler, _) = Build("http://oms.local:5002");   // no trailing slash

        await dispatcher.DispatchAsync(
            "oms", Row(callbackPath: "api/shipments/abc/arrived", callbackMethod: "put"),
            CancellationToken.None);

        handler.Last!.RequestUri!.ToString().Should().Be("http://oms.local:5002/api/shipments/abc/arrived");
        handler.Last.Method.Should().Be(HttpMethod.Put);
    }

    // 2026-08 — reactive token refresh: a 401 (idle-expired session at the
    // receiver) force-mints once and retries with the RE-READ credential.
    [Fact]
    public async Task Dispatch_401ThenRefreshed_RetriesOnceWithNewToken()
    {
        var (dispatcher, handler, refresher) = Build("http://oms.local:5002",
            BearerCred("http://oms.local:5002", "stale-token"),
            BearerCred("http://oms.local:5002", "fresh-token"));
        handler.Script(HttpStatusCode.Unauthorized);   // then default 200
        refresher.RefreshAsync("oms", true, Arg.Any<CancellationToken>())
            .Returns(DTMS.Iam.Application.Callbacks.RefreshResult.Refreshed(DateTime.UtcNow.AddHours(1)));

        await dispatcher.DispatchAsync("oms", Row(), CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("stale-token");
        handler.Requests[1].Headers.Authorization!.Parameter.Should().Be("fresh-token");
        await refresher.Received(1).RefreshAsync("oms", true, Arg.Any<CancellationToken>());
    }

    // Refresh couldn't mint (not configured / rejected / lock busy) → fail
    // normally after ONE attempt; the outbox retry ladder owns what's next.
    [Fact]
    public async Task Dispatch_401AndRefreshSkipped_ThrowsWithoutRetry()
    {
        var (dispatcher, handler, refresher) = Build("http://oms.local:5002");
        handler.Script(HttpStatusCode.Unauthorized);

        var act = async () => await dispatcher.DispatchAsync("oms", Row(), CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.Requests.Should().HaveCount(1);
        await refresher.Received(1).RefreshAsync("oms", true, Arg.Any<CancellationToken>());
    }

    // A second 401 after a successful mint must NOT loop — one reactive
    // retry per dispatch, then the failure surfaces.
    [Fact]
    public async Task Dispatch_401Twice_DoesNotLoop()
    {
        var (dispatcher, handler, refresher) = Build("http://oms.local:5002",
            BearerCred("http://oms.local:5002", "stale-token"),
            BearerCred("http://oms.local:5002", "fresh-token"));
        handler.Script(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        refresher.RefreshAsync("oms", true, Arg.Any<CancellationToken>())
            .Returns(DTMS.Iam.Application.Callbacks.RefreshResult.Refreshed(DateTime.UtcNow.AddHours(1)));

        var act = async () => await dispatcher.DispatchAsync("oms", Row(), CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.Requests.Should().HaveCount(2);
        await refresher.Received(1).RefreshAsync("oms", true, Arg.Any<CancellationToken>());
    }
}
