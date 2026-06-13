using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderDispatched;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanned;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RecomputeOrderStatus;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobAnchor;
using AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobDispatched;
using AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobFailed;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
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

        // Phase 1b (b8): Create a 1:1 Job anchor per group before the order
        // is marked Planned. Best-effort — a failed anchor logs a warning
        // and proceeds without a JobId; the group still dispatches normally
        // but won't be retriable via /jobs/{id}/retry.
        var jobIdByGroup = new Dictionary<int, Guid>();
        foreach (var (groupIndex, stationGroup) in stationGroups.Index())
        {
            var jobResult = await _sender.Send(new CreateJobAnchorCommand(
                evt.DeliveryOrderId,
                GroupIndex: groupIndex + 1,
                stationGroup.Key.PickupStationId,
                stationGroup.Key.DropStationId,
                Priority: "Normal",
                RequestedTransportMode: evt.RequestedTransportMode,
                SlaDeadline: evt.LatestUtc), ct);

            if (jobResult.IsSuccess)
                jobIdByGroup[groupIndex] = jobResult.Value;
            else
                _logger.LogWarning(
                    "[AutoPlan] Job anchor failed for group {G} ({Pickup} → {Drop}): {Err}",
                    groupIndex + 1, stationGroup.Key.PickupStationId,
                    stationGroup.Key.DropStationId, jobResult.Error);
        }

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
            var jobId = jobIdByGroup.GetValueOrDefault(groupIndex);

            var envelopeResult = await _envelopeDispatch.DispatchByRouteAsync(
                evt.DeliveryOrderId,
                stationGroup.Key.PickupStationId,
                stationGroup.Key.DropStationId,
                upperKey,
                appointVehicleKeyOverride: null,
                jobId: jobId == Guid.Empty ? null : jobId,
                cancellationToken: ct);

            if (envelopeResult.IsSuccess && envelopeResult.Value.TripId != Guid.Empty)
            {
                successCount++;
                _logger.LogInformation(
                    "[AutoPlan] ✓ Group {G} dispatched via envelope template '{Template}' " +
                    "(upperKey {UpperKey} → vendorOrderKey {VendorKey}, tripId {TripId})",
                    groupIndex + 1, envelopeResult.Value.TemplateName, upperKey,
                    envelopeResult.Value.VendorOrderKey, envelopeResult.Value.TripId);

                if (jobId != Guid.Empty)
                    await _sender.Send(new MarkJobDispatchedCommand(
                        jobId, envelopeResult.Value.TripId, envelopeResult.Value.VendorOrderKey), ct);
            }
            else if (envelopeResult.IsSuccess && envelopeResult.Value.TripId == Guid.Empty)
            {
                // Orphan: vendor accepted but Trip persistence failed.
                // Items stay Pending (physically on vendor) and order
                // proceeds to Dispatched, but the Job is marked Failed
                // so ops can run reconciliation.
                successCount++;
                var orphanReason =
                    $"vendor accepted (key={envelopeResult.Value.VendorOrderKey}) " +
                    "but trip persistence failed — reconciliation required";
                _logger.LogError(
                    "[AutoPlan] ⚠ Group {G} ({Pickup} → {Drop}) orphan: {Reason}",
                    groupIndex + 1, stationGroup.Key.PickupStationId,
                    stationGroup.Key.DropStationId, orphanReason);

                if (jobId != Guid.Empty)
                    await _sender.Send(new MarkJobFailedCommand(
                        jobId, orphanReason, JobFailureCategory.TripPersistenceFailed), ct);
            }
            else
            {
                _logger.LogWarning(
                    "[AutoPlan] ✗ Group {G} ({Pickup} → {Drop}) failed: {Reason}",
                    groupIndex + 1, stationGroup.Key.PickupStationId,
                    stationGroup.Key.DropStationId, envelopeResult.Error);

                if (jobId != Guid.Empty)
                    await _sender.Send(new MarkJobFailedCommand(
                        jobId, envelopeResult.Error ?? "vendor rejected dispatch",
                        JobFailureCategory.VendorRejected), ct);

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
            // All groups failed dispatch — every item is already Failed
            // via MarkGroupItemsAsDispatchFailed, but no Trip exists so
            // there's no TripCompleted/Failed/Cancelled consumer to
            // trigger RecomputeStatusFromItems for us. Without an
            // explicit kick the order would sit at "Planned" with all
            // items Failed indefinitely (Bug #2 from manual E2E test).
            _logger.LogWarning("[AutoPlan] ═══ Order {OrderId}: all {Total} groups failed dispatch ═══",
                evt.DeliveryOrderId, stationGroups.Count);
            await _sender.Send(new RecomputeOrderStatusCommand(evt.DeliveryOrderId), ct);
        }
    }
}
