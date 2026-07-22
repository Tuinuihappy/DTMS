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
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpSourceCallbackDispatcher dispatcher, CapturingHandler handler) Build(string baseUrl)
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);

        var cache = Substitute.For<ITieredCache>();
        cache.GetAsync<CachedCredential>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CachedCredential
            {
                SystemKey = "oms",
                CallbackBaseUrl = baseUrl,
                CallbackTimeoutMs = 5000,   // non-zero — 0 would time out immediately
                // CallbackAuthScheme null → no auth header applied
            });
        var reader = new CachedCredentialReader(
            cache, Substitute.For<ISystemCredentialRepository>(),
            new DTMS.Iam.Infrastructure.Security.CallbackTokenProtector(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));

        var dispatcher = new HttpSourceCallbackDispatcher(
            http, reader, NullLogger<HttpSourceCallbackDispatcher>.Instance);
        return (dispatcher, handler);
    }

    private static OutboxMessage Row(string? callbackPath = null, string? callbackMethod = null) =>
        new(Guid.NewGuid(), "shipment.started.v1", "{}", DateTime.UtcNow,
            partitionKey: "oms", callbackPath: callbackPath, callbackMethod: callbackMethod);

    [Fact]
    public async Task Dispatch_DefaultsToEventsPost_WhenRouteNull()
    {
        var (dispatcher, handler) = Build("http://oms.local:5002/");

        await dispatcher.DispatchAsync("oms", Row(), CancellationToken.None);

        handler.Last!.RequestUri!.ToString().Should().Be("http://oms.local:5002/events");
        handler.Last.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task Dispatch_UsesCallbackPath_WhenSet()
    {
        var (dispatcher, handler) = Build("http://oms.local:5002/");   // trailing slash trimmed

        await dispatcher.DispatchAsync("oms", Row(callbackPath: "/api/shipments"), CancellationToken.None);

        handler.Last!.RequestUri!.ToString().Should().Be("http://oms.local:5002/api/shipments");
    }

    [Fact]
    public async Task Dispatch_NormalizesLeadingSlash_AndHonorsMethodOverride()
    {
        var (dispatcher, handler) = Build("http://oms.local:5002");   // no trailing slash

        await dispatcher.DispatchAsync(
            "oms", Row(callbackPath: "api/shipments/abc/arrived", callbackMethod: "put"),
            CancellationToken.None);

        handler.Last!.RequestUri!.ToString().Should().Be("http://oms.local:5002/api/shipments/abc/arrived");
        handler.Last.Method.Should().Be(HttpMethod.Put);
    }
}
