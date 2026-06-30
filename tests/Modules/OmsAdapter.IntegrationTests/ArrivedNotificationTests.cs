using DTMS.OmsAdapter.Abstractions.Models;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OmsAdapter.IntegrationTests;

// Phase OMS B3 — Arrived-stage HTTP integration tests.
// Pin the shape of POST /api/shipments/{id}/arrived: the shipmentId
// belongs in the URL path, NOT the body — body only carries lots.
public class ArrivedNotificationTests : IClassFixture<HttpOmsShipmentClientFixture>
{
    private readonly HttpOmsShipmentClientFixture _fx;

    public ArrivedNotificationTests(HttpOmsShipmentClientFixture fx) => _fx = fx;

    [Fact]
    public async Task NotifyShipmentArrivedAsync_ServerReturns200_PostsShipmentIdInPathLotsInBody()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        var lots = new[] { new OmsLot("LOT-A"), new OmsLot("LOT-B") };
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/arrived")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await _fx.Client.NotifyShipmentArrivedAsync(_fx.Target,shipmentId, lots, CancellationToken.None);

        var log = _fx.Server.LogEntries.Single();
        log.RequestMessage.Path.Should().Be($"/api/shipments/{shipmentId}/arrived");
        log.RequestMessage.Method.Should().Be("POST");
        // shipmentId belongs only in the URL — the body is lots-only.
        log.RequestMessage.Body.Should().NotContain("\"shipmentId\"");
        log.RequestMessage.Body.Should().Contain("\"lotNo\":\"LOT-A\"");
        log.RequestMessage.Body.Should().Contain("\"lotNo\":\"LOT-B\"");
    }

    [Fact]
    public async Task NotifyShipmentArrivedAsync_ServerReturns500_ThrowsHttpRequestException()
    {
        _fx.Server.Reset();
        var shipmentId = Guid.NewGuid().ToString();
        _fx.Server.Given(Request.Create()
                .WithPath($"/api/shipments/{shipmentId}/arrived")
                .UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var act = () => _fx.Client.NotifyShipmentArrivedAsync(_fx.Target,
            shipmentId, new[] { new OmsLot("LOT-X") }, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task NotifyShipmentArrivedAsync_EmptyShipmentId_ThrowsArgumentException()
    {
        var act = () => _fx.Client.NotifyShipmentArrivedAsync(_fx.Target,
            "", Array.Empty<OmsLot>(), CancellationToken.None);

        // Defensive client-side validation — never let a malformed
        // shipmentId hit the wire and create an orphan URL.
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
