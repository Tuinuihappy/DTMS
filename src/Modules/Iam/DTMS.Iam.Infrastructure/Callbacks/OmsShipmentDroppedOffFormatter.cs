using System.Globalization;
using System.Text.Json;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Formats a <see cref="ShipmentDroppedOffContext"/> into the body OMS's TMS
/// integration endpoint expects at
/// <c>POST /integrations/tms/shipments/{shipmentId}/dropoff-arrived</c>:
/// <c>{ "orderRef": ..., "locationCode": ..., "occurredAt": ... }</c>
/// (shipmentId travels in the path). Replaces OmsShipmentArrivedFormatter's
/// legacy <c>/api/shipments/{id}/arrived</c> lot-list contract (2026-08).
/// This formatter IS the contract; the byte-compat tests pin it.
/// Resolved by keyed DI under <see cref="FormatKey"/>.
/// </summary>
public sealed class OmsShipmentDroppedOffFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.droppedoff.v1";

    // OMS's example pins millisecond precision — same rationale as the
    // started/pickedup formatters: a raw DateTime would serialise 7
    // fractional digits, which a strict parser may reject.
    private const string OccurredAtFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct)
    {
        if (integrationEvent is not ShipmentDroppedOffContext ctx)
            throw new InvalidOperationException(
                $"{nameof(OmsShipmentDroppedOffFormatter)} expects {nameof(ShipmentDroppedOffContext)} " +
                $"but received {integrationEvent.GetType().Name}.");

        // Bus round-trips can strip DateTimeKind — normalise so the wire value
        // is always UTC with the literal 'Z' the format string appends. Every
        // producer stamps UTC, so an Unspecified kind is a UTC value that lost
        // its kind marker: re-stamp it rather than ToUniversalTime(), which
        // would treat it as server-local and shift it by the UTC offset.
        var occurredAtUtc = ctx.OccurredAt.Kind switch
        {
            DateTimeKind.Utc => ctx.OccurredAt,
            DateTimeKind.Local => ctx.OccurredAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(ctx.OccurredAt, DateTimeKind.Utc),
        };

        // Field order matters: the byte-compat tests pin it.
        var payload = new
        {
            orderRef = ctx.OrderRef,
            locationCode = ctx.LocationCode,
            occurredAt = occurredAtUtc.ToString(OccurredAtFormat, CultureInfo.InvariantCulture),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        // shipmentId in the path — escaped defensively (Guid string today).
        var path = $"/integrations/tms/shipments/{Uri.EscapeDataString(ctx.ShipmentId)}/dropoff-arrived";
        return Task.FromResult(new CallbackPayload(
            "application/json", json, RelativePath: path));
    }
}
