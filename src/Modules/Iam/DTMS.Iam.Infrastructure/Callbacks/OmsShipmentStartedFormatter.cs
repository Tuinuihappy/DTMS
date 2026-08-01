using System.Globalization;
using System.Text.Json;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Formats a <see cref="ShipmentStartedContext"/> into the body OMS's TMS
/// integration endpoint expects at <c>POST /integrations/tms/shipments/started</c>:
/// <c>{ "shipmentId": ..., "orderRef": ..., "deliveryBy": ..., "occurredAt": ... }</c>.
/// This formatter IS the contract (2026-08 revision — the legacy
/// <c>/api/shipments</c> lot-list shape is gone); the byte-compat tests pin it.
/// Routes via <see cref="CallbackPayload.RelativePath"/>; resolved by keyed DI
/// under <see cref="FormatKey"/>.
/// </summary>
public sealed class OmsShipmentStartedFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.started.v1";
    public const string ShipmentsPath = "/integrations/tms/shipments/started";

    // OMS's example pins millisecond precision ("...T02:17:41.263Z"). A raw
    // DateTime would serialise with 7 fractional digits, which a strict
    // parser on their side may reject — format explicitly instead.
    private const string OccurredAtFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct)
    {
        if (integrationEvent is not ShipmentStartedContext ctx)
            throw new InvalidOperationException(
                $"{nameof(OmsShipmentStartedFormatter)} expects {nameof(ShipmentStartedContext)} " +
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
        // Nulls are written (no ignore condition) so deliveryBy=null
        // serialises explicitly — pool/self-managed sends rely on that.
        var payload = new
        {
            shipmentId = ctx.ShipmentId,
            orderRef = ctx.OrderRef,
            deliveryBy = ctx.DeliveryBy,
            occurredAt = occurredAtUtc.ToString(OccurredAtFormat, CultureInfo.InvariantCulture),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        return Task.FromResult(new CallbackPayload(
            "application/json", json, RelativePath: ShipmentsPath));
    }
}
