using System.Text;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// Phase S.5 — golden wire-format for the OMS shipment formatters; the expected
// JSON is pinned inline so the OMS contract can't drift. Started, pickedup and
// droppedoff use the 2026-08 TMS-integration contracts
// (/integrations/tms/shipments/*); cancelled still pins the legacy
// /api/shipments/{id}/cancelled shape until OMS moves that route too.
public class OmsShipmentFormatterByteCompatTests
{
    private static async Task<string> Body(ICallbackPayloadFormatter f, object ctx) =>
        Encoding.UTF8.GetString((await f.FormatAsync(ctx, CancellationToken.None)).Body);

    // 2026-08 contract — POST /integrations/tms/shipments/started with
    // {shipmentId, orderRef, deliveryBy, occurredAt}. occurredAt is pinned to
    // millisecond precision with a literal Z (OMS's example: "...T02:17:41.263Z");
    // a raw DateTime would serialise 7 fractional digits.
    [Fact]
    public async Task Started_BodyMatchesContract_AndRoutesToTmsIntegrationPath()
    {
        var payload = await new OmsShipmentStartedFormatter().FormatAsync(
            new ShipmentStartedContext("root-trip-1", "OD-2607-0001", "FAN1_STANDARD_NO3",
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Utc)),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"shipmentId\":\"root-trip-1\",\"orderRef\":\"OD-2607-0001\"," +
            "\"deliveryBy\":\"FAN1_STANDARD_NO3\",\"occurredAt\":\"2026-07-15T09:07:09.263Z\"}");
        payload.RelativePath.Should().Be("/integrations/tms/shipments/started");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    // deliveryBy=null is a deliberate shape (self-managed order without
    // RequestedBy; pool trip started before any operator claimed it).
    [Fact]
    public async Task Started_NullDeliveryBy_SerializesNull()
    {
        var body = await Body(new OmsShipmentStartedFormatter(),
            new ShipmentStartedContext("root-trip-1", "OD-2607-0001", null,
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Utc)));

        body.Should().Be(
            "{\"shipmentId\":\"root-trip-1\",\"orderRef\":\"OD-2607-0001\"," +
            "\"deliveryBy\":null,\"occurredAt\":\"2026-07-15T09:07:09.263Z\"}");
    }

    // A bus round-trip can hand the formatter Kind=Unspecified for a value that
    // was stamped UTC at the source — it must be re-stamped, not shifted by the
    // server's local offset, and still serialise with the trailing Z.
    [Fact]
    public async Task Started_UnspecifiedKind_TreatedAsUtc_NotShifted()
    {
        var body = await Body(new OmsShipmentStartedFormatter(),
            new ShipmentStartedContext("root-trip-1", "OD-2607-0001", "FAN1_STANDARD_NO3",
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Unspecified)));

        body.Should().Contain("\"occurredAt\":\"2026-07-15T09:07:09.263Z\"");
    }

    // 2026-08 — pickup contract: shipmentId in the PATH (like arrived), body
    // {orderRef, locationCode, occurredAt} with millisecond-pinned occurredAt.
    [Fact]
    public async Task PickedUp_BodyMatchesContract_AndShipmentIdInPath()
    {
        var payload = await new OmsShipmentPickedUpFormatter().FormatAsync(
            new ShipmentPickedUpContext("root-trip-7", "OD-2607-0001", "WH-A",
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Utc)),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"orderRef\":\"OD-2607-0001\",\"locationCode\":\"WH-A\"," +
            "\"occurredAt\":\"2026-07-15T09:07:09.263Z\"}");
        payload.RelativePath.Should().Be("/integrations/tms/shipments/root-trip-7/pickup-arrived");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    [Fact]
    public async Task PickedUp_UnspecifiedKind_TreatedAsUtc_NotShifted()
    {
        var payload = await new OmsShipmentPickedUpFormatter().FormatAsync(
            new ShipmentPickedUpContext("root-trip-7", "OD-2607-0001", "WH-A",
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Unspecified)),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body)
            .Should().Contain("\"occurredAt\":\"2026-07-15T09:07:09.263Z\"");
    }

    [Fact]
    public async Task PickedUpFormatter_RejectsWrongContextType()
    {
        var act = async () => await new OmsShipmentPickedUpFormatter()
            .FormatAsync("not-a-context", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // 2026-08 — drop-off contract (renamed from arrived): shipmentId in the
    // PATH, body {orderRef, locationCode, occurredAt} with millisecond-pinned
    // occurredAt. The legacy /api/shipments/{id}/arrived lot-list shape is gone.
    [Fact]
    public async Task DroppedOff_BodyMatchesContract_AndShipmentIdInPath()
    {
        var payload = await new OmsShipmentDroppedOffFormatter().FormatAsync(
            new ShipmentDroppedOffContext("root-trip-9", "OD-2607-0001", "STF_09",
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Utc)),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body).Should().Be(
            "{\"orderRef\":\"OD-2607-0001\",\"locationCode\":\"STF_09\"," +
            "\"occurredAt\":\"2026-07-15T09:07:09.263Z\"}");
        payload.RelativePath.Should().Be("/integrations/tms/shipments/root-trip-9/dropoff-arrived");
        payload.HttpMethod.Should().BeNull();   // → dispatcher default POST
    }

    [Fact]
    public async Task DroppedOff_UnspecifiedKind_TreatedAsUtc_NotShifted()
    {
        var payload = await new OmsShipmentDroppedOffFormatter().FormatAsync(
            new ShipmentDroppedOffContext("root-trip-9", "OD-2607-0001", "STF_09",
                new DateTime(2026, 7, 15, 9, 7, 9, 263, DateTimeKind.Unspecified)),
            CancellationToken.None);

        Encoding.UTF8.GetString(payload.Body)
            .Should().Contain("\"occurredAt\":\"2026-07-15T09:07:09.263Z\"");
    }

    [Fact]
    public async Task DroppedOffFormatter_RejectsWrongContextType()
    {
        var act = async () => await new OmsShipmentDroppedOffFormatter()
            .FormatAsync("not-a-context", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
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
        OmsShipmentPickedUpFormatter.FormatKey.Should().Be("oms.shipment.pickedup.v1");
        // droppedoff's DB literal is written by the 20260801170000 rename
        // migration (UPDATE), not a seed INSERT.
        OmsShipmentDroppedOffFormatter.FormatKey.Should().Be("oms.shipment.droppedoff.v1");
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
