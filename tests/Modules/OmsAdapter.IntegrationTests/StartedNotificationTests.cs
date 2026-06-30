using System.Net;
using DTMS.OmsAdapter.Abstractions.Models;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OmsAdapter.IntegrationTests;

// Phase OMS B3 — Started-stage HTTP integration tests.
// Pin the existing happy/409/5xx behavior of NotifyShipmentStartedAsync
// before B4 reshapes the client, so the failure-stage extensions can't
// regress what already ships.
public class StartedNotificationTests : IClassFixture<HttpOmsShipmentClientFixture>
{
    private readonly HttpOmsShipmentClientFixture _fx;

    public StartedNotificationTests(HttpOmsShipmentClientFixture fx) => _fx = fx;

    private static OmsShipmentNotification SampleNotification(string? shipmentId = null) => new(
        ShipmentId: shipmentId ?? Guid.NewGuid().ToString(),
        DeliveryBy: "AMR",
        Lots: new[] { new OmsLot("LOT-001"), new OmsLot("LOT-002") });

    [Fact]
    public async Task NotifyShipmentStartedAsync_ServerReturns200_Completes()
    {
        _fx.Server.Reset();
        var notification = SampleNotification();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await _fx.Client.NotifyShipmentStartedAsync(_fx.Target,notification, CancellationToken.None);

        var log = _fx.Server.LogEntries.Single();
        log.RequestMessage.Path.Should().Be("/api/shipments");
        log.RequestMessage.Method.Should().Be("POST");
        log.RequestMessage.Body.Should().Contain(notification.ShipmentId);
        log.RequestMessage.Body.Should().Contain("\"deliveryBy\":\"AMR\"");
        log.RequestMessage.Body.Should().Contain("\"lotNo\":\"LOT-001\"");
    }

    [Fact]
    public async Task NotifyShipmentStartedAsync_ServerReturns201Created_Completes()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));

        // 2xx is "success" — 201 is just as valid as 200.
        await _fx.Client.NotifyShipmentStartedAsync(_fx.Target,SampleNotification(), CancellationToken.None);
        _fx.Server.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyShipmentStartedAsync_ServerReturns409Conflict_TreatedAsIdempotentSuccess()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409).WithBody("already registered"));

        // Option A behavior: 409 means "OMS already has this shipmentId" —
        // never throw, never dead-letter. A retry that re-fires the same
        // shipmentId must succeed without operator follow-up.
        await _fx.Client.NotifyShipmentStartedAsync(_fx.Target,SampleNotification(), CancellationToken.None);
        _fx.Server.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyShipmentStartedAsync_ServerReturns500_ThrowsHttpRequestException()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("boom"));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(_fx.Target,SampleNotification(), CancellationToken.None);

        // 5xx MUST throw so the MassTransit Fault consumer picks it up and
        // writes the *NotifyFailed audit row. Swallowing would leave the
        // UI showing "Awaiting…" forever with no resend path.
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task NotifyShipmentStartedAsync_ServerReturns503_ThrowsHttpRequestException()
    {
        _fx.Server.Reset();
        _fx.Server.Given(Request.Create().WithPath("/api/shipments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var act = () => _fx.Client.NotifyShipmentStartedAsync(_fx.Target,SampleNotification(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
