using System.Text.Json;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Iam.Application.Callbacks;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Phase S.3.1b follow-up — sibling of
/// <see cref="OmsShipmentDeliveredFormatter"/> for the cancellation
/// flow. Resolved by keyed DI under <see cref="FormatKey"/>; admin
/// wires the subscription with this key when OMS wants the cancel
/// callback.
///
/// <para>Kept as a separate formatter (vs one type-switching
/// formatter) so each event type's payload contract can evolve
/// independently — OMS may want extra fields on cancel (reason,
/// triggeredBy) that don't apply to the happy path.</para>
/// </summary>
public sealed class OmsShipmentCancelledFormatter : ICallbackPayloadFormatter
{
    public const string FormatKey = "oms.shipment.cancel.v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct)
    {
        if (integrationEvent is not DeliveryOrderCancelledIntegrationEventV1 evt)
            throw new InvalidOperationException(
                $"{nameof(OmsShipmentCancelledFormatter)} expects {nameof(DeliveryOrderCancelledIntegrationEventV1)} " +
                $"but received {integrationEvent.GetType().Name}.");

        var payload = new
        {
            shipmentId = evt.DeliveryOrderId,
            status = "cancelled",
            cancelledAt = evt.OccurredOn,
            reason = evt.Reason,
            eventId = evt.EventId,
            triggeredBy = evt.TriggeredBy,
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        return Task.FromResult(new CallbackPayload("application/json", json));
    }
}
