using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OmsAdapter.IntegrationTests;

// Phase OMS B3/B4 — Trip-failed HTTP integration tests. shipmentId in
// URL path, body carries failureReason + failureCategory + occurredAt.
// 409 Conflict is idempotent success (retry of same shipmentId/stage
// must not dead-letter).
public class TripFailedNotificationTests : IClassFixture<HttpOmsShipmentClientFixture>
{
    private readonly HttpOmsShipmentClientFixture _fx;

    public TripFailedNotificationTests(HttpOmsShipmentClientFixture fx) => _fx = fx;

    private static OmsTripFailedNotification SampleBody() => new(
        FailureReason: "vendor robot disconnected after 3 minutes",
        FailureCategory: "TripFailed",
        OccurredAt: new DateTime(2026, 6, 16, 10, 30, 0, DateTimeKind.Utc));

    [Fact]
    public async Task NotifyShipmentFailedAsync_ServerReturns200_PostsToCorrectPath()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/failed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await _fx.Client.NotifyShipmentFailedAsync(shipmentId, SampleBody(), CancellationToken.None);

        var log = _fx.Server.LogEntries.Single();
        log.RequestMessage.Path.Should().Be($"/api/shipments/{shipmentId}/failed");
        log.RequestMessage.Method.Should().Be("POST");
        log.RequestMessage.Body.Should().Contain("\"failureReason\":\"vendor robot disconnected after 3 minutes\"");
        log.RequestMessage.Body.Should().Contain("\"failureCategory\":\"TripFailed\"");
        log.RequestMessage.Body.Should().Contain("\"occurredAt\":\"2026-06-16T10:30:00Z\"");
        // shipmentId belongs only in the URL — mirror /arrived convention.
        log.RequestMessage.Body.Should().NotContain("\"shipmentId\"");
    }

    [Fact]
    public async Task NotifyShipmentFailedAsync_ServerReturns409Conflict_TreatedAsIdempotentSuccess()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/failed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409).WithBody("already failed"));

        // Retry safety: re-firing the same shipmentId after dead-letter
        // recovery must not throw. OMS dedupes on its side.
        await _fx.Client.NotifyShipmentFailedAsync(shipmentId, SampleBody(), CancellationToken.None);
        _fx.Server.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyShipmentFailedAsync_ServerReturns500_ThrowsHttpRequestException()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/failed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var act = () => _fx.Client.NotifyShipmentFailedAsync(shipmentId, SampleBody(), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task NotifyShipmentFailedAsync_EmptyShipmentId_ThrowsArgumentException()
    {
        var act = () => _fx.Client.NotifyShipmentFailedAsync(
            "", SampleBody(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
