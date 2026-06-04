using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.SharedKernel;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Auto Planning Pipeline (envelope-only — legacy job/leg/task path
/// removed in Phase b7):
///   DeliveryOrder (Confirmed) → Group items by station pair → for each
///   group, look up an active OrderTemplate for the route and POST the
///   RIOT3 envelope. Vendor takes over execution; callbacks land via
///   webhook (Riot3Webhooks.HandleEnvelopeTaskEvent). Groups without a
///   matching template are rejected with a warning — ops must register
///   a template before re-confirming the order.
/// </summary>
public class DeliveryOrderValidatedConsumer : IConsumer<DeliveryOrderConfirmedIntegrationEventV1>
{
    private readonly IDispatchOrderTemplateService _envelopeDispatch;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;

    public DeliveryOrderValidatedConsumer(
        IDispatchOrderTemplateService envelopeDispatch,
        ILogger<DeliveryOrderValidatedConsumer> logger)
    {
        _envelopeDispatch = envelopeDispatch;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> context)
    {
        var evt = context.Message;

        var stationGroups = evt.Items
            .GroupBy(i => (i.PickupStationId, i.DropStationId))
            .ToList();

        _logger.LogInformation("[AutoPlan] Order {OrderId} with {ItemCount} item(s) in {GroupCount} station group(s) (transport mode: {Mode}, SLA deadline: {Sla})",
            evt.DeliveryOrderId, evt.Items.Count, stationGroups.Count,
            evt.RequestedTransportMode ?? "(unspecified)",
            evt.LatestUtc?.ToString("o") ?? "(none)");

        foreach (var (groupIndex, stationGroup) in stationGroups.Index())
        {
            var items = stationGroup.ToList();

            _logger.LogInformation("[AutoPlan] Group {G}: {Count} item(s) ({Pickup} → {Drop})",
                groupIndex + 1, items.Count,
                stationGroup.Key.PickupStationId, stationGroup.Key.DropStationId);

            // Composite upperKey scheme: see EnvelopeUpperKey for the
            // format. RIOT3 echoes this back in every webhook.
            var upperKey = EnvelopeUpperKey.Build(evt.DeliveryOrderId, groupIndex + 1);

            var envelopeResult = await _envelopeDispatch.DispatchByRouteAsync(
                evt.DeliveryOrderId,
                stationGroup.Key.PickupStationId,
                stationGroup.Key.DropStationId,
                upperKey,
                appointVehicleKeyOverride: null,
                cancellationToken: context.CancellationToken);

            if (envelopeResult.IsSuccess)
            {
                _logger.LogInformation(
                    "[AutoPlan] ✓ Group {G} dispatched via envelope template '{Template}' (upperKey {UpperKey} → vendorOrderKey {VendorKey}, tripId {TripId})",
                    groupIndex + 1, envelopeResult.Value.TemplateName, upperKey, envelopeResult.Value.VendorOrderKey, envelopeResult.Value.TripId);
            }
            else
            {
                _logger.LogWarning(
                    "[AutoPlan] ✗ Group {G} ({Pickup} → {Drop}) skipped: {Reason}. Register an OrderTemplate for this route and re-confirm.",
                    groupIndex + 1, stationGroup.Key.PickupStationId, stationGroup.Key.DropStationId, envelopeResult.Error);
            }
        }

        _logger.LogInformation("[AutoPlan] ═══ Pipeline complete for Order {OrderId} ═══", evt.DeliveryOrderId);
    }
}
