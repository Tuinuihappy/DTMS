using System.Text.Json;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Phase S.5 (B2) — formats an <see cref="ShipmentStartedContext"/> into the
/// exact body the legacy OMS adapter POSTed to <c>/api/shipments</c>:
/// <c>{ "shipmentId": ..., "deliveryBy": ..., "lots": [{ "lotNo": ... }] }</c>
/// (byte-identical to <c>OmsShipmentNotification</c>). Routes to the legacy
/// path via <see cref="CallbackPayload.RelativePath"/> so OMS's endpoint is
/// unchanged. Resolved by keyed DI under <see cref="FormatKey"/>.
/// </summary>
public sealed class OmsShipmentStartedFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.started.v1";
    public const string ShipmentsPath = "/api/shipments";

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

        // Field names + order match the legacy OmsShipmentNotification record
        // (shipmentId, deliveryBy, lots[ { lotNo } ]). Nulls are written (no
        // ignore condition) so deliveryBy=null serialises identically.
        var payload = new
        {
            shipmentId = ctx.ShipmentId,
            deliveryBy = ctx.DeliveryBy,
            lots = ctx.LotNos.Select(l => new { lotNo = l }).ToArray(),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        return Task.FromResult(new CallbackPayload(
            "application/json", json, RelativePath: ShipmentsPath));
    }
}
