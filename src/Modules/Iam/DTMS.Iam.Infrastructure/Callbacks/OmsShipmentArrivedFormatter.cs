using System.Text.Json;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Phase S.5 (B2) — formats an <see cref="ShipmentArrivedContext"/> into the
/// legacy OMS <c>/api/shipments/{shipmentId}/arrived</c> call: the shipmentId
/// travels in the URL path (resolved here into
/// <see cref="CallbackPayload.RelativePath"/>) and only the lots travel in the
/// body — <c>{ "lots": [{ "lotNo": ... }] }</c>, byte-identical to
/// <c>OmsArrivedNotification</c>. Resolved by keyed DI under
/// <see cref="FormatKey"/>.
/// </summary>
public sealed class OmsShipmentArrivedFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.arrived.v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct)
    {
        if (integrationEvent is not ShipmentArrivedContext ctx)
            throw new InvalidOperationException(
                $"{nameof(OmsShipmentArrivedFormatter)} expects {nameof(ShipmentArrivedContext)} " +
                $"but received {integrationEvent.GetType().Name}.");

        var payload = new
        {
            lots = ctx.LotNos.Select(l => new { lotNo = l }).ToArray(),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        // shipmentId in the path — escaped defensively (Guid string today).
        var path = $"/api/shipments/{Uri.EscapeDataString(ctx.ShipmentId)}/arrived";
        return Task.FromResult(new CallbackPayload(
            "application/json", json, RelativePath: path));
    }
}
