using System.Text;
using System.Text.Json;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Phase S.3.1b — turns
/// <see cref="DeliveryOrderCompletedIntegrationEventV1"/> into the
/// payload shape OMS's <c>/dtms-callbacks/events</c> endpoint expects.
/// Resolved by keyed DI under <see cref="FormatKey"/>; bound to
/// <c>iam.SystemEventSubscriptions.PayloadFormatKey</c>.
///
/// <para>OMS payload kept minimal — only what OMS needs to look up the
/// shipment on their side:
/// <c>shipmentId</c> (DTMS DeliveryOrderId, the canonical token they
/// got back at create time) and <c>deliveredAt</c>. They join back to
/// their own ORD-* by the mapping they stored on POST. Schema bump
/// only when their side breaks.</para>
/// </summary>
public sealed class OmsShipmentDeliveredFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct)
    {
        if (integrationEvent is not DeliveryOrderCompletedIntegrationEventV1 evt)
            throw new InvalidOperationException(
                $"{nameof(OmsShipmentDeliveredFormatter)} expects {nameof(DeliveryOrderCompletedIntegrationEventV1)} " +
                $"but received {integrationEvent.GetType().Name}.");

        var payload = new
        {
            shipmentId = evt.DeliveryOrderId,
            status = "delivered",
            deliveredAt = evt.OccurredOn,
            eventId = evt.EventId,
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        return Task.FromResult(new CallbackPayload("application/json", json));
    }
}
