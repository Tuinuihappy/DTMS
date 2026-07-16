using System.Text;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// Phase S.5 — golden wire-format for the OMS shipment formatters. These bytes
// were byte-identical to the legacy OmsShipmentNotification / OmsArrivedNotification
// serialization; after the legacy adapter was removed (Phase 4) the expected
// JSON is pinned inline so the OMS contract can't drift.
public class OmsShipmentFormatterByteCompatTests
{
    private static async Task<string> Body(ICallbackPayloadFormatter f, object ctx) =>
        Encoding.UTF8.GetString((await f.FormatAsync(ctx, CancellationToken.None)).Body);

    [Fact]
    public async Task Started_BodyMatchesContract_AndRoutesToShipmentsPath()
    {
        var payload = await new OmsShipmentStartedFormatter().FormatAsync(
            new ShipmentStartedContext("root-trip-1", "FAN1_STANDARD_NO3",
                new[] { "LOT-A", "LOT-B" }), CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"shipmentId\":\"root-trip-1\",\"deliveryBy\":\"FAN1_STANDARD_NO3\"," +
            "\"lots\":[{\"lotNo\":\"LOT-A\"},{\"lotNo\":\"LOT-B\"}]}");
        payload.RelativePath.Should().Be("/api/shipments");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    [Fact]
    public async Task Started_NullDeliveryBy_SerializesNull()
    {
        var body = await Body(new OmsShipmentStartedFormatter(),
            new ShipmentStartedContext("root-trip-1", null, new[] { "LOT-A" }));

        body.Should().Be(
            "{\"shipmentId\":\"root-trip-1\",\"deliveryBy\":null,\"lots\":[{\"lotNo\":\"LOT-A\"}]}");
    }

    [Fact]
    public async Task Arrived_BodyMatchesContract_AndShipmentIdInPath()
    {
        var payload = await new OmsShipmentArrivedFormatter().FormatAsync(
            new ShipmentArrivedContext("root-trip-9", new[] { "LOT-A", "LOT-B" }),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"lots\":[{\"lotNo\":\"LOT-A\"},{\"lotNo\":\"LOT-B\"}]}");
        payload.RelativePath.Should().Be("/api/shipments/root-trip-9/arrived");
    }

    // Wire-identical to the OmsTripCancelledNotification that 0f123c2 deleted, so
    // OMS can restore the route it already had rather than agree a new contract.
    // The field names are OMS's — cancelReason, not reason.
    [Fact]
    public async Task Cancelled_BodyMatchesLegacyContract_AndShipmentIdInPath()
    {
        var payload = await new OmsShipmentCancelledFormatter().FormatAsync(
            new ShipmentCancelledContext(
                "root-trip-9", "vendor cancelled", "86347852",
                new DateTime(2026, 7, 15, 9, 7, 9, DateTimeKind.Utc)),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"cancelReason\":\"vendor cancelled\",\"cancelledBy\":\"86347852\"," +
            "\"occurredAt\":\"2026-07-15T09:07:09Z\"}");
        payload.RelativePath.Should().Be("/api/shipments/root-trip-9/cancelled");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    // TriggeredBy is nullable on the event (vendor-initiated cancels carry no
    // actor); the legacy DTO's cancelledBy was nullable for the same reason.
    [Fact]
    public async Task Cancelled_NullCancelledBy_SerializesNull()
    {
        var body = await Body(new OmsShipmentCancelledFormatter(),
            new ShipmentCancelledContext(
                "root-trip-9", "vendor cancelled", null,
                new DateTime(2026, 7, 15, 9, 7, 9, DateTimeKind.Utc)));

        body.Should().Be(
            "{\"cancelReason\":\"vendor cancelled\",\"cancelledBy\":null," +
            "\"occurredAt\":\"2026-07-15T09:07:09Z\"}");
    }

    [Fact]
    public async Task Formatter_RejectsWrongContextType()
    {
        var act = async () => await new OmsShipmentStartedFormatter()
            .FormatAsync("not-a-context", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancelledFormatter_RejectsWrongContextType()
    {
        var act = async () => await new OmsShipmentCancelledFormatter()
            .FormatAsync("not-a-context", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // FormatKey is coupled to a plain string in the DB: the fan-out resolves the
    // formatter with GetRequiredKeyedService(sub.PayloadFormatKey), and that key
    // is seeded as a SQL literal by the subscription migration. Renaming a const
    // keeps DI happy (the registration reads the const) while every callback
    // throws at resolve time — and only once the subscription is enabled, which
    // may be months after the rename. Pin both ends.
    [Fact]
    public void FormatKeys_MatchTheValuesSeededInMigrations()
    {
        OmsShipmentStartedFormatter.FormatKey.Should().Be("oms.shipment.started.v1");
        OmsShipmentArrivedFormatter.FormatKey.Should().Be("oms.shipment.arrived.v1");
        OmsShipmentCancelledFormatter.FormatKey.Should().Be("oms.shipment.cancelled.v1");
    }

    // Same coupling one layer up: EventType is a SQL literal in the seed and is
    // validated against All on the subscription-create path.
    [Fact]
    public void EventTypeRegistry_ContainsShipmentCancelled()
    {
        CallbackEventTypes.ShipmentCancelledV1.Should().Be("shipment.cancelled.v1");
        CallbackEventTypes.All.Should().Contain("shipment.cancelled.v1");
        CallbackEventTypes.IsKnown("shipment.cancelled.v1").Should().BeTrue();
    }
}
