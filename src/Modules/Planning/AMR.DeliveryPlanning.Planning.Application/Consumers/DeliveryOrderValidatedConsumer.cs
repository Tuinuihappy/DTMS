using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderDispatched;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanned;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RecomputeOrderStatus;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobAnchor;
using AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobDispatched;
using AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobFailed;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel;
using AMR.DeliveryPlanning.SharedKernel.Diagnostics;
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
    private readonly IDispatchStrategyRegistry _strategyRegistry;
    private readonly ISender _sender;
    private readonly WorkflowMetrics _metrics;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;

    public DeliveryOrderValidatedConsumer(
        IDispatchStrategyRegistry strategyRegistry,
        ISender sender,
        WorkflowMetrics metrics,
        ILogger<DeliveryOrderValidatedConsumer> logger)
    {
        _strategyRegistry = strategyRegistry;
        _sender = sender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Phase 3c — route through IDispatchStrategy by transport mode.
        // AMR uses station Ids on the integration event; Manual / Fleet
        // will eventually use warehouse Ids (the strategy contract carries
        // both for forward compatibility). For now non-AMR stub strategies
        // return Failure with a "not yet implemented" message, which lands
        // the order at Failed with a clear reason — better than Confirmed-
        // forever (the Phase 3a stopgap).
        var mode = ParseMode(evt.RequestedTransportMode);
        if (!_strategyRegistry.IsRegistered(mode))
        {
            _logger.LogWarning(
                "[AutoPlan] Order {OrderId} mode '{Mode}' has no registered IDispatchStrategy. " +
                "Marking order Failed so it doesn't stall at Confirmed.",
                evt.DeliveryOrderId, mode);
            await _sender.Send(new MarkOrderPlanningCommand(evt.DeliveryOrderId), ct);
            await _sender.Send(new RecomputeOrderStatusCommand(evt.DeliveryOrderId), ct);
            return;
        }
        var strategy = _strategyRegistry.Get(mode);

        // Manual / Fleet orders carry null station Ids (warehouse-keyed
        // resolution will come with the Phase 4 mode-aware grouping). For
        // now treat them as a single group keyed at Guid.Empty — the stub
        // strategy ignores the values and returns "not implemented" so the
        // failure path below marks everything Failed coherently.
        var stationGroups = evt.Items
            .GroupBy(i => (
                PickupStationId: i.PickupStationId ?? Guid.Empty,
                DropStationId:   i.DropStationId   ?? Guid.Empty))
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

        // T2 Phase 2 step 1 — also publish OrderPlanRequested for the saga
        // shadow path. When Workflow:UseSaga=false (default) the event is
        // published but has no subscribers, so it's a no-op. When the flag
        // is on, DeliveryOrderSagaStateMachine consumes it to transition
        // AwaitingPlan → Planning. Publishing AFTER MarkOrderPlanning
        // succeeds means a transient failure won't tell the saga planning
        // started when it didn't; if the consumer retries, the saga's Step 1
        // Ignore handler on Planning state will dedupe the redelivery.
        await context.Publish(new OrderPlanRequestedIntegrationEventV1(
            EventId: Guid.NewGuid(),
            OccurredOn: DateTime.UtcNow,
            DeliveryOrderId: evt.DeliveryOrderId), ct);

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
        //
        // T1.2 — every group iteration is wrapped in try/catch with structured
        // logging. OperationCanceledException is rethrown immediately so a host
        // shutdown lets MassTransit redeliver the message to a healthy pod;
        // all other exceptions are caught, the job is marked Failed, and the
        // loop continues so unrelated groups still get a chance. If even the
        // MarkJobFailed call faults we log critical and rethrow — we cannot
        // leave the job stuck at Created silently (that was the OD-0374 bug).
        var successCount = 0;
        foreach (var (groupIndex, stationGroup) in stationGroups.Index())
        {
            var items = stationGroup.ToList();
            var upperKey = EnvelopeUpperKey.Build(evt.DeliveryOrderId, groupIndex + 1);
            var jobId = jobIdByGroup.GetValueOrDefault(groupIndex);

            using var groupScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["OrderId"] = evt.DeliveryOrderId,
                ["JobId"] = jobId == Guid.Empty ? null : jobId,
                ["GroupIndex"] = groupIndex + 1,
                ["UpperKey"] = upperKey,
            });

            _logger.LogInformation("[AutoPlan] Group {G}: {Count} item(s) ({Pickup} → {Drop}) Step=Dispatching",
                groupIndex + 1, items.Count,
                stationGroup.Key.PickupStationId, stationGroup.Key.DropStationId);

            try
            {
                // Phase 3c — dispatch routes through IDispatchStrategy
                // resolved per mode. AMR delegates back to the existing
                // DispatchByRouteAsync (OrderTemplate → RIOT3); Manual / Fleet
                // route to their own stubs/impls. Same Result<DispatchGroupOutcome>
                // shape so the success / orphan / vendor-rejected branches below
                // stay mode-agnostic.
                // Manual / Fleet strategies key off warehouse Ids — read
                // them off the first item (the grouping key guarantees the
                // station pair is shared; Phase 2.5 Path A guarantees the
                // warehouse pair is too).
                var firstGroupItem = items.First();
                var envelopeResult = await strategy.DispatchGroupAsync(
                    new DispatchGroupRequest(
                        DeliveryOrderId: evt.DeliveryOrderId,
                        GroupIndex: groupIndex + 1,
                        PickupStationId: stationGroup.Key.PickupStationId,
                        DropStationId: stationGroup.Key.DropStationId,
                        UpperKey: upperKey,
                        JobId: jobId == Guid.Empty ? null : jobId,
                        PickupWarehouseId: firstGroupItem.PickupWarehouseId,
                        DropWarehouseId: firstGroupItem.DropWarehouseId,
                        SlaDeadline: evt.LatestUtc),
                    ct);

                if (envelopeResult.IsSuccess && envelopeResult.Value.TripId != Guid.Empty)
                {
                    successCount++;
                    _logger.LogInformation(
                        "[AutoPlan] ✓ Group {G} dispatched via envelope template '{Template}' " +
                        "(upperKey {UpperKey} → vendorOrderKey {VendorKey}, tripId {TripId}) Step=MarkJobDispatched",
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
                        "[AutoPlan] ⚠ Group {G} ({Pickup} → {Drop}) orphan: {Reason} Step=MarkJobFailed-Orphan",
                        groupIndex + 1, stationGroup.Key.PickupStationId,
                        stationGroup.Key.DropStationId, orphanReason);

                    if (jobId != Guid.Empty)
                        await _sender.Send(new MarkJobFailedCommand(
                            jobId, orphanReason, JobFailureCategory.TripPersistenceFailed), ct);
                }
                else
                {
                    _logger.LogWarning(
                        "[AutoPlan] ✗ Group {G} ({Pickup} → {Drop}) failed: {Reason} Step=MarkJobFailed-VendorRejected",
                        groupIndex + 1, stationGroup.Key.PickupStationId,
                        stationGroup.Key.DropStationId, envelopeResult.Error);

                    if (jobId != Guid.Empty)
                        await _sender.Send(new MarkJobFailedCommand(
                            jobId, envelopeResult.Error ?? "vendor rejected dispatch",
                            JobFailureCategory.VendorRejected), ct);

                    // Mark this group's items Failed so the order's eventual
                    // RecomputeStatusFromItems isn't blocked on them. Pass
                    // BOTH location pairs — AMR items match by station,
                    // Manual / Fleet match by warehouse (Bug A fix). The
                    // first item in the group is representative since the
                    // grouping key guarantees a shared station pair, and
                    // warehouse pairs follow station pairs (or vice versa)
                    // by Phase 2.5 Path A's validation rules.
                    var firstItem = stationGroup.First();
                    await _sender.Send(new MarkGroupItemsAsDispatchFailedCommand(
                        evt.DeliveryOrderId,
                        stationGroup.Key.PickupStationId,
                        stationGroup.Key.DropStationId,
                        firstItem.PickupWarehouseId,
                        firstItem.DropWarehouseId,
                        envelopeResult.Error ?? "vendor rejected dispatch"), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Host is stopping (T1.3 graceful shutdown) — let MassTransit
                // redeliver. Commands run so far are idempotent (T1.5), so the
                // redelivered message will skip them and resume from this group.
                _logger.LogWarning(
                    "[AutoPlan] ↺ Group {G} cancelled mid-dispatch — message will be redelivered",
                    groupIndex + 1);
                throw;
            }
            catch (Exception ex)
            {
                // Unexpected fault from DispatchByRouteAsync or a downstream
                // command. Mark THIS group's job Failed (best-effort) so the
                // order can converge to a terminal state instead of orphaning
                // the Job at Status=Created (the OD-0374 stuck-Planned bug).
                // Other groups still attempt — if the fault is infra-wide and
                // they all throw, MassTransit's UseMessageRetry (T1.1) will
                // redeliver and idempotent guards (T1.5) make that safe.
                _metrics.RecordDispatchException(ex.GetType().Name);
                _logger.LogError(ex,
                    "[AutoPlan] ✗ Group {G} dispatch threw {ExceptionType} Step=DispatchByRoute",
                    groupIndex + 1, ex.GetType().Name);

                if (jobId != Guid.Empty)
                {
                    try
                    {
                        await _sender.Send(new MarkJobFailedCommand(
                            jobId,
                            $"dispatch threw {ex.GetType().Name}: {ex.Message}",
                            JobFailureCategory.DispatchException), ct);
                    }
                    catch (Exception markEx) when (markEx is not OperationCanceledException)
                    {
                        // If we can't even mark the job failed the system is in
                        // a bad state — rethrow so MassTransit retries the whole
                        // consumer rather than leaving a silently-orphaned Job.
                        _logger.LogCritical(markEx,
                            "[AutoPlan] ✗✗ Group {G} failed to MarkJobFailed after dispatch threw — rethrowing for redelivery",
                            groupIndex + 1);
                        throw;
                    }
                }

                // Also mark the items Failed so the order doesn't sit forever
                // on Pending items waiting for a Trip that will never exist.
                // Pass both station + warehouse Ids (Bug A fix — Manual orders
                // match by warehouse).
                try
                {
                    var firstItem = stationGroup.First();
                    await _sender.Send(new MarkGroupItemsAsDispatchFailedCommand(
                        evt.DeliveryOrderId,
                        stationGroup.Key.PickupStationId,
                        stationGroup.Key.DropStationId,
                        firstItem.PickupWarehouseId,
                        firstItem.DropWarehouseId,
                        $"dispatch threw {ex.GetType().Name}"), ct);
                }
                catch (Exception markEx) when (markEx is not OperationCanceledException)
                {
                    _logger.LogError(markEx,
                        "[AutoPlan] ✗ Group {G} failed to mark items as dispatch-failed — items may stay Pending",
                        groupIndex + 1);
                }
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

    // The integration event carries RequestedTransportMode as a string
    // (sourced from the order's PascalCase enum.ToString()). Parse
    // permissively so a malformed value or null lands on Amr, the most
    // common case, instead of crashing the consumer.
    private static TransportMode ParseMode(string? raw) =>
        Enum.TryParse<TransportMode>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : TransportMode.Amr;
}
