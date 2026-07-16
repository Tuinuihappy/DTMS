using System.Text.Json;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Formats a <see cref="ShipmentCancelledContext"/> into OMS's
/// <c>POST /api/shipments/{shipmentId}/cancelled</c> call — shipmentId in the
/// path, the rest in the body, mirroring <see cref="OmsShipmentArrivedFormatter"/>.
/// Resolved by keyed DI under <see cref="FormatKey"/>.
///
/// <para>Path and body are not a new contract: they reproduce the
/// <c>OmsTripCancelledNotification</c> DTMS posted to this exact route until
/// 0f123c2 tore the outbound chain out (OMS had dropped the endpoint). Wire-
/// identical on purpose — if OMS restores the route, nothing here needs to
/// change. Do not "tidy" the field names; <c>cancelReason</c>/<c>cancelledBy</c>
/// are OMS's, not ours.</para>
///
/// <para>The shipmentId is the root trip id the fan-out resolved, the same token
/// OMS received from <c>shipment.started.v1</c> — and the same one the old
/// TripCancelledOmsNotifyConsumer sent. An interim revision of this formatter
/// took the raw order event and sent <c>DeliveryOrderId</c> instead — an id OMS
/// has never seen — and set no RelativePath, so it POSTed to <c>/events</c>,
/// which OMS does not expose. It was never subscribed, so it never fired.
/// FormatKey changed with the fix (<c>oms.shipment.cancel.v1</c> →
/// <c>oms.shipment.cancelled.v1</c>) so no stale subscription row can resolve to
/// this class and silently inherit that contract.</para>
/// </summary>
public sealed class OmsShipmentCancelledFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.cancelled.v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct)
    {
        if (integrationEvent is not ShipmentCancelledContext ctx)
            throw new InvalidOperationException(
                $"{nameof(OmsShipmentCancelledFormatter)} expects {nameof(ShipmentCancelledContext)} " +
                $"but received {integrationEvent.GetType().Name}.");

        var payload = new
        {
            cancelReason = ctx.Reason,
            cancelledBy = ctx.CancelledBy,
            occurredAt = ctx.OccurredAt,
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        // shipmentId in the path — escaped defensively (Guid string today).
        var path = $"/api/shipments/{Uri.EscapeDataString(ctx.ShipmentId)}/cancelled";
        return Task.FromResult(new CallbackPayload(
            "application/json", json, RelativePath: path));
    }
}
