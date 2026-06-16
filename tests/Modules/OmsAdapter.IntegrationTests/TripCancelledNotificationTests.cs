using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OmsAdapter.IntegrationTests;

// Phase OMS B3/B4 — Trip-cancelled HTTP integration tests.
public class TripCancelledNotificationTests : IClassFixture<HttpOmsShipmentClientFixture>
{
    private readonly HttpOmsShipmentClientFixture _fx;

    public TripCancelledNotificationTests(HttpOmsShipmentClientFixture fx) => _fx = fx;

    private static OmsTripCancelledNotification SampleBody() => new(
        CancelReason: "customer no-show at pickup window",
        CancelledBy: "ops-supervisor-7",
        OccurredAt: new DateTime(2026, 6, 16, 11, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task NotifyShipmentCancelledAsync_ServerReturns200_PostsToCorrectPath()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/cancelled")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await _fx.Client.NotifyShipmentCancelledAsync(shipmentId, SampleBody(), CancellationToken.None);

        var log = _fx.Server.LogEntries.Single();
        log.RequestMessage.Path.Should().Be($"/api/shipments/{shipmentId}/cancelled");
        log.RequestMessage.Method.Should().Be("POST");
        log.RequestMessage.Body.Should().Contain("\"cancelReason\":\"customer no-show at pickup window\"");
        log.RequestMessage.Body.Should().Contain("\"cancelledBy\":\"ops-supervisor-7\"");
        log.RequestMessage.Body.Should().Contain("\"occurredAt\":\"2026-06-16T11:00:00Z\"");
    }

    [Fact]
    public async Task NotifyShipmentCancelledAsync_NullCancelledBy_StillSerialisesField()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        var body = new OmsTripCancelledNotification(
            CancelReason: "system timeout", CancelledBy: null,
            OccurredAt: DateTime.UtcNow);
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/cancelled")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await _fx.Client.NotifyShipmentCancelledAsync(shipmentId, body, CancellationToken.None);

        // Receiver needs to see a stable field set across all messages —
        // null vs omitted matters for some downstream serialisers.
        _fx.Server.LogEntries.Single().RequestMessage.Body
            .Should().Contain("\"cancelledBy\":null");
    }

    [Fact]
    public async Task NotifyShipmentCancelledAsync_ServerReturns409Conflict_TreatedAsIdempotentSuccess()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/cancelled")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409));

        await _fx.Client.NotifyShipmentCancelledAsync(shipmentId, SampleBody(), CancellationToken.None);
        _fx.Server.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyShipmentCancelledAsync_ServerReturns503_ThrowsHttpRequestException()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/cancelled")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        var act = () => _fx.Client.NotifyShipmentCancelledAsync(shipmentId, SampleBody(), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
