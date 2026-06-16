using AMR.DeliveryPlanning.OmsAdapter.Abstractions.Models;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OmsAdapter.IntegrationTests;

// Phase OMS B3/B4 — POD-completed HTTP integration tests.
public class PodCompletedNotificationTests : IClassFixture<HttpOmsShipmentClientFixture>
{
    private readonly HttpOmsShipmentClientFixture _fx;

    public PodCompletedNotificationTests(HttpOmsShipmentClientFixture fx) => _fx = fx;

    private static OmsPodCompletedNotification SampleBody() => new(
        ScannedIds: new[] { "LOT-AA-001", "LOT-AA-002", "LOT-BB-003" },
        ScannedAt: new DateTime(2026, 6, 16, 11, 30, 0, DateTimeKind.Utc));

    [Fact]
    public async Task NotifyShipmentPodCompletedAsync_ServerReturns200_PostsToCorrectPath()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/pod-completed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await _fx.Client.NotifyShipmentPodCompletedAsync(shipmentId, SampleBody(), CancellationToken.None);

        var log = _fx.Server.LogEntries.Single();
        log.RequestMessage.Path.Should().Be($"/api/shipments/{shipmentId}/pod-completed");
        log.RequestMessage.Method.Should().Be("POST");
        log.RequestMessage.Body.Should().Contain("\"scannedIds\":[\"LOT-AA-001\",\"LOT-AA-002\",\"LOT-BB-003\"]");
        log.RequestMessage.Body.Should().Contain("\"scannedAt\":\"2026-06-16T11:30:00Z\"");
    }

    [Fact]
    public async Task NotifyShipmentPodCompletedAsync_EmptyScannedIds_StillPostsBody()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        var body = new OmsPodCompletedNotification(
            ScannedIds: Array.Empty<string>(),
            ScannedAt: DateTime.UtcNow);
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/pod-completed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        // The consumer guards against empty scans (skip), but the client
        // itself MUST stay shape-stable — if a future caller passes an
        // empty list (e.g. failure-mode POD with no scans), the request
        // shape stays valid for OMS-side deserialisation.
        await _fx.Client.NotifyShipmentPodCompletedAsync(shipmentId, body, CancellationToken.None);
        _fx.Server.LogEntries.Single().RequestMessage.Body
            .Should().Contain("\"scannedIds\":[]");
    }

    [Fact]
    public async Task NotifyShipmentPodCompletedAsync_ServerReturns409Conflict_TreatedAsIdempotentSuccess()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/pod-completed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409));

        await _fx.Client.NotifyShipmentPodCompletedAsync(shipmentId, SampleBody(), CancellationToken.None);
        _fx.Server.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyShipmentPodCompletedAsync_ServerReturns500_ThrowsHttpRequestException()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/pod-completed")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var act = () => _fx.Client.NotifyShipmentPodCompletedAsync(shipmentId, SampleBody(), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
