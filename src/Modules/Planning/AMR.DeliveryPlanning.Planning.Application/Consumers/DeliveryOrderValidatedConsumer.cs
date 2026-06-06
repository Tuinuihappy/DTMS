using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderDispatched;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanned;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.SharedKernel;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Auto Planning Pipeline (envelope-only — legacy job/leg/task path
/// removed in Phase b7):
///   DeliveryOrder (Confirmed) → MarkPlanning → group items + resolve
///   templates → MarkPlanned → loop dispatch each group → MarkDispatched
///   (if any group succeeded). Groups that fail at the vendor have
///   their items marked Failed immediately so the order can eventually
///   reach PartiallyCompleted / Failed instead of stalling on Pending
///   items. The 4-state progression (Planning / Planned / Dispatched /
///   InProgress) drives Order-level visibility for operator dashboards.
/// </summary>
public class DeliveryOrderValidatedConsumer : IConsumer<DeliveryOrderConfirmedIntegrationEventV1>
{
    private readonly IDispatchOrderTemplateService _envelopeDispatch;
    private readonly ISender _sender;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;

    public DeliveryOrderValidatedConsumer(
        IDispatchOrderTemplateService envelopeDispatch,
        ISender sender,
        ILogger<DeliveryOrderValidatedConsumer> logger)
    {
        _envelopeDispatch = envelopeDispatch;
        _sender = sender;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        var stationGroups = evt.Items
            .GroupBy(i => (i.PickupStationId, i.DropStationId))
            .ToList();

        _logger.LogInformation(
            "[AutoPlan] Order {OrderId} with {ItemCount} item(s) in {GroupCount} station group(s) " +
            "(transport mode: {Mode}, SLA deadline: {Sla})",
            evt.DeliveryOrderId, evt.Items.Count, stationGroups.Count,
            evt.RequestedTransportMode ?? "(unspecified)",
            evt.LatestUtc?.ToString("o") ?? "(none)");

        // Phase 1: Confirmed → Planning. Internal event (sub-second), but
        // surfaces in audit + lets RabbitMQ redeliveries be idempotent.
        await _sender.Send(new MarkOrderPlanningCommand(evt.DeliveryOrderId), ct);

        // Phase 2: Planning → Planned. Group + template resolution happen
        // inside DispatchByRouteAsync per group; the explicit Planned mark
        // serves as the "no more pre-vendor work needed" boundary.
        await _sender.Send(new MarkOrderPlannedCommand(evt.DeliveryOrderId), ct);

        // Phase 3: dispatch each group. Track which succeeded so we can
        // (a) advance to Dispatched only when ≥1 trip is in vendor hands
        // and (b) mark items of failed groups Failed so the order can
        // eventually reach terminal.
        var successCount = 0;
        foreach (var (groupIndex, stationGroup) in stationGroups.Index())
        {
            var items = stationGroup.ToList();
            _logger.LogInformation("[AutoPlan] Group {G}: {Count} item(s) ({Pickup} → {Drop})",
                groupIndex + 1, items.Count,
                stationGroup.Key.PickupStationId, stationGroup.Key.DropStationId);

            var upperKey = EnvelopeUpperKey.Build(evt.DeliveryOrderId, groupIndex + 1);

            var envelopeResult = await _envelopeDispatch.DispatchByRouteAsync(
                evt.DeliveryOrderId,
                stationGroup.Key.PickupStationId,
                stationGroup.Key.DropStationId,
                upperKey,
                appointVehicleKeyOverride: null,
                cancellationToken: ct);

            if (envelopeResult.IsSuccess)
            {
                successCount++;
                _logger.LogInformation(
                    "[AutoPlan] ✓ Group {G} dispatched via envelope template '{Template}' " +
                    "(upperKey {UpperKey} → vendorOrderKey {VendorKey}, tripId {TripId})",
                    groupIndex + 1, envelopeResult.Value.TemplateName, upperKey,
                    envelopeResult.Value.VendorOrderKey, envelopeResult.Value.TripId);
            }
            else
            {
                _logger.LogWarning(
                    "[AutoPlan] ✗ Group {G} ({Pickup} → {Drop}) failed: {Reason}",
                    groupIndex + 1, stationGroup.Key.PickupStationId,
                    stationGroup.Key.DropStationId, envelopeResult.Error);

                // Mark this group's items Failed so the order's eventual
                // RecomputeStatusFromItems isn't blocked on them.
                await _sender.Send(new MarkGroupItemsAsDispatchFailedCommand(
                    evt.DeliveryOrderId,
                    stationGroup.Key.PickupStationId,
                    stationGroup.Key.DropStationId,
                    envelopeResult.Error ?? "vendor rejected dispatch"), ct);
            }
        }

        // Phase 4: Planned → Dispatched (any success) OR stay Planned and
        // let item-level Failed propagate via RecomputeStatusFromItems.
        if (successCount > 0)
        {
            await _sender.Send(new MarkOrderDispatchedCommand(evt.DeliveryOrderId), ct);
            _logger.LogInformation("[AutoPlan] ═══ Order {OrderId} → Dispatched ({OK}/{Total} groups) ═══",
                evt.DeliveryOrderId, successCount, stationGroups.Count);
        }
        else
        {
            // All groups failed — the per-group MarkGroupItemsAsDispatchFailed
            // calls already moved every item to Failed. The next
            // RecomputeStatusFromItems trigger (or the order Confirmed
            // event mapper) will surface this as Order = Failed.
            _logger.LogWarning("[AutoPlan] ═══ Order {OrderId}: all {Total} groups failed dispatch ═══",
                evt.DeliveryOrderId, stationGroups.Count);
        }
    }
}
