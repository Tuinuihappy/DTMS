using DTMS.DeliveryOrder.Application.Commands.MarkAllItemsAsDispatchFailed;
using DTMS.DeliveryOrder.Application.Commands.MarkGroupItemsAsDispatchFailed;
using DTMS.DeliveryOrder.Application.Commands.MarkOrderDispatched;
using DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanned;
using DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using DTMS.DeliveryOrder.Application.Commands.RecomputeOrderStatus;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Planning.Application.Commands.CreateJobAnchor;
using DTMS.Planning.Application.Commands.MarkJobDispatched;
using DTMS.Planning.Application.Commands.MarkJobFailed;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.IntegrationEvents;
using DTMS.SharedKernel;
using DTMS.SharedKernel.Diagnostics;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using TripStatus = DTMS.Dispatch.Domain.Enums.TripStatus;

namespace DTMS.Planning.Application.Consumers;

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
    private readonly ISelfManagedDispatchService _selfManaged;
    private readonly ISender _sender;
    private readonly WorkflowMetrics _metrics;
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;

    public DeliveryOrderValidatedConsumer(
        IDispatchStrategyRegistry strategyRegistry,
        ISelfManagedDispatchService selfManaged,
        ISender sender,
        WorkflowMetrics metrics,
        ITripRepository tripRepository,
        ILogger<DeliveryOrderValidatedConsumer> logger)
    {
        _strategyRegistry = strategyRegistry;
        _selfManaged = selfManaged;
        _sender = sender;
        _metrics = metrics;
        _tripRepository = tripRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        // Phase 3c — route through IDispatchStrategy by transport mode.
        // AMR uses station Ids on the integration event; Manual uses WMS
        // location Ids (the strategy contract carries both).
        var mode = ParseMode(evt.RequestedTransportMode);
        if (!_strategyRegistry.IsRegistered(mode))
        {
            // No strategy → no groups ever exist, so the group-pair fail
            // command can't apply. Fail every pending item, then recompute:
            // Confirmed passes the recompute guards and delivered==0 lands
            // the order at Failed. Redelivery is a no-op (0 items marked,
            // recompute early-returns on Failed).
            _logger.LogWarning(
                "[AutoPlan] Order {OrderId} mode '{Mode}' has no registered IDispatchStrategy. " +
                "Failing all pending items so the order recomputes to Failed instead of stalling at Confirmed.",
                evt.DeliveryOrderId, mode);
            var reason = new TransportModeNotEnabledException(mode).Message;
            await _sender.Send(new MarkAllItemsAsDispatchFailedCommand(evt.DeliveryOrderId, reason), ct);
            await _sender.Send(new RecomputeOrderStatusCommand(evt.DeliveryOrderId), ct);
            return;
        }
        var strategy = _strategyRegistry.Get(mode);

        // Delegate grouping to the per-mode strategy — AMR groups by
        // station pair, Manual by warehouse pair. The consumer stays mode-agnostic;
        // adding a new TransportMode means implementing GroupItems on
        // the new strategy, no changes here.
        var groupingInput = evt.Items
            .Select(i => new DispatchGroupItem(
                ItemId: i.ItemId,
                PickupStationId:     i.PickupStationId,
                DropStationId:       i.DropStationId,
                PickupWmsLocationId: i.PickupWmsLocationId,
                DropWmsLocationId:   i.DropWmsLocationId))
            .ToList();
        var groups = strategy.GroupItems(groupingInput);

        _logger.LogInformation(
            "[AutoPlan] Order {OrderId} with {ItemCount} item(s) in {GroupCount} dispatch group(s) " +
            "(transport mode: {Mode}, SLA deadline: {Sla})",
            evt.DeliveryOrderId, evt.Items.Count, groups.Count,
            evt.RequestedTransportMode ?? "(unspecified)",
            evt.LatestUtc?.ToString("o") ?? "(none)");

        // Phase 1: Confirmed → Planning. Internal event (sub-second), but
        // surfaces in audit + lets RabbitMQ redeliveries be idempotent.
        //
        // Bail when the order can no longer enter Planning. A cancel landing
        // while this event is in flight otherwise leaves the order Cancelled and
        // this consumer about to dispatch trips for it — and nothing downstream
        // would stop them, because the order-cancelled cascade already ran and
        // found no trips. Same for a Held order, which until now was dispatched
        // regardless, making the hold do nothing; releasing one re-raises the
        // Confirmed event, so it plans then.
        //
        // Swallowing the event on failure is safe ONLY because
        // MarkOrderPlanningCommandHandler catches InvalidOperationException and
        // nothing else — a DbUpdateConcurrencyException propagates and
        // MassTransit redelivers. So IsFailure always means "the domain said no",
        // never "try again". If a transient catch is ever added there, this turns
        // from a race guard into a silent order-dropper.
        //
        // Idempotent on redelivery: MarkPlanning is forward-only, returning
        // success for Planning / Planned / Dispatched / InProgress.
        var planningResult = await _sender.Send(new MarkOrderPlanningCommand(evt.DeliveryOrderId), ct);
        if (planningResult.IsFailure)
        {
            _logger.LogWarning(
                "[AutoPlan] Order {OrderId} cannot enter Planning ({Error}) — abandoning dispatch.",
                evt.DeliveryOrderId, planningResult.Error);
            return;
        }

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
        foreach (var (groupIndex, group) in groups.Index())
        {
            var jobResult = await _sender.Send(new CreateJobAnchorCommand(
                evt.DeliveryOrderId,
                GroupIndex: groupIndex + 1,
                group.PickupStationId ?? Guid.Empty,
                group.DropStationId   ?? Guid.Empty,
                Priority: "Normal",
                RequestedTransportMode: evt.RequestedTransportMode,
                SlaDeadline: evt.LatestUtc), ct);

            if (jobResult.IsSuccess)
                jobIdByGroup[groupIndex] = jobResult.Value;
            else
                _logger.LogWarning(
                    "[AutoPlan] Job anchor failed for group {G} ({Pickup} → {Drop}): {Err}",
                    groupIndex + 1,
                    (object?)group.PickupStationId ?? group.PickupWmsLocationId,
                    (object?)group.DropStationId   ?? group.DropWmsLocationId,
                    jobResult.Error);
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
        // A re-plan (revise-and-resend, admin redispatch) finds the earlier
        // attempts' trips here; a first plan finds none.
        var priorTrips = await _tripRepository.GetAttemptRefsByDeliveryOrderIdAsync(evt.DeliveryOrderId, ct);

        var successCount = 0;
        foreach (var (groupIndex, group) in groups.Index())
        {
            var items = group.Items;
            var plan = NextAttempt(priorTrips, evt.DeliveryOrderId, groupIndex + 1);
            if (plan.Skip)
            {
                // Its items are still bound to that trip, so the group counts
                // as in vendor hands for the order's Dispatched transition.
                successCount++;
                _logger.LogInformation(
                    "[AutoPlan] Group {G} already has trip {TripId} ({Status}) — not re-dispatching",
                    groupIndex + 1, plan.ExistingTrip!.Id, plan.ExistingTrip.Status);
                continue;
            }
            var (attemptNumber, previousAttemptId) = (plan.AttemptNumber, plan.PreviousAttemptId);
            var upperKey = EnvelopeUpperKey.Build(evt.DeliveryOrderId, groupIndex + 1, attemptNumber);
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
                (object?)group.PickupStationId ?? group.PickupWmsLocationId,
                (object?)group.DropStationId   ?? group.DropWmsLocationId);

            try
            {
                // Strategy already populated the pair fields for its mode:
                // AMR fills PickupStationId/DropStationId, Manual fills the
                // warehouse pair. Pass both through — DispatchGroupAsync
                // implementations ignore the pair they don't use.
                var dispatchRequest = new DispatchGroupRequest(
                    DeliveryOrderId: evt.DeliveryOrderId,
                    GroupIndex: groupIndex + 1,
                    PickupStationId: group.PickupStationId ?? Guid.Empty,
                    DropStationId:   group.DropStationId   ?? Guid.Empty,
                    UpperKey: upperKey,
                    JobId: jobId == Guid.Empty ? null : jobId,
                    AttemptNumber: attemptNumber,
                    PreviousAttemptId: previousAttemptId,
                    SlaDeadline: evt.LatestUtc,
                    PickupWmsLocationId: group.PickupWmsLocationId,
                    DropWmsLocationId:   group.DropWmsLocationId,
                    RequestedBy: evt.RequestedBy);

                // Self-managed orders (source system runs the transport itself)
                // route to the self-managed path: create trip + auto ack +
                // pickup, no vendor/pool. Supported for Manual mode only — it
                // replaces the operator-pool execution, not AMR's RIOT3
                // lifecycle. Order creation already rejects self-managed +
                // non-Manual; the mode gate here is defence in depth so a
                // stray event can't self-dispatch an AMR trip. Grouping above
                // still used the Manual strategy (WMS pair).
                var selfManaged = evt.SelfManaged && mode == TransportMode.Manual;
                var envelopeResult = selfManaged
                    ? await _selfManaged.DispatchGroupAsync(dispatchRequest, ct)
                    : await strategy.DispatchGroupAsync(dispatchRequest, ct);

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
                        groupIndex + 1,
                        (object?)group.PickupStationId ?? group.PickupWmsLocationId,
                        (object?)group.DropStationId   ?? group.DropWmsLocationId,
                        orphanReason);

                    if (jobId != Guid.Empty)
                        await _sender.Send(new MarkJobFailedCommand(
                            jobId, orphanReason, JobFailureCategory.TripPersistenceFailed), ct);
                }
                else
                {
                    _logger.LogWarning(
                        "[AutoPlan] ✗ Group {G} ({Pickup} → {Drop}) failed: {Reason} Step=MarkJobFailed-VendorRejected",
                        groupIndex + 1,
                        (object?)group.PickupStationId ?? group.PickupWmsLocationId,
                        (object?)group.DropStationId   ?? group.DropWmsLocationId,
                        envelopeResult.Error);

                    if (jobId != Guid.Empty)
                        await _sender.Send(new MarkJobFailedCommand(
                            jobId, envelopeResult.Error ?? "vendor rejected dispatch",
                            JobFailureCategory.VendorRejected), ct);

                    // Mark this group's items Failed using the strategy's
                    // grouping key — AMR matches by station, Manual by
                    // warehouse. Pass both pairs; the command's matcher
                    // checks the non-null pair.
                    await _sender.Send(new MarkGroupItemsAsDispatchFailedCommand(
                        evt.DeliveryOrderId,
                        group.PickupStationId ?? Guid.Empty,
                        group.DropStationId   ?? Guid.Empty,
                        group.PickupWmsLocationId,
                        group.DropWmsLocationId,
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
                // Use the group's strategy-populated pair (station for AMR,
                // warehouse for Manual).
                try
                {
                    await _sender.Send(new MarkGroupItemsAsDispatchFailedCommand(
                        evt.DeliveryOrderId,
                        group.PickupStationId ?? Guid.Empty,
                        group.DropStationId   ?? Guid.Empty,
                        group.PickupWmsLocationId,
                        group.DropWmsLocationId,
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
                evt.DeliveryOrderId, successCount, groups.Count);
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
                evt.DeliveryOrderId, groups.Count);
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

    /// <summary>What to do with one dispatch group, given the order's earlier trips.</summary>
    /// <param name="Skip">The group already has a running or completed trip — dispatch nothing.</param>
    /// <param name="ExistingTrip">The chain tip when skipping (for the log line).</param>
    internal sealed record GroupAttempt(
        bool Skip, int AttemptNumber, Guid? PreviousAttemptId, TripAttemptRef? ExistingTrip = null);

    /// <summary>
    /// Decides a group's dispatch from the tip of its retry chain:
    /// <list type="bullet">
    ///   <item>No earlier trip (every first plan) → attempt 1, the historical upperKey.</item>
    ///   <item>Tip Failed / Cancelled / Rejected (revise-and-resend, admin
    ///         redispatch) → one past the tip, linked to it. Reusing the old
    ///         upperKey would make RIOT3 run a robot (it doesn't dedupe) while
    ///         CreateEnvelopeTrip, idempotent on upperKey, hands back the dead
    ///         trip — so the run's webhooks and the source's shipment callbacks
    ///         would attach to it. The link keeps the source's shipmentId (root
    ///         of the chain), exactly like a trip retry.</item>
    ///   <item>Tip still running, or Completed → skip. The Confirmed event is
    ///         re-raised by Release (an order can be held mid-run) and redelivered
    ///         by MassTransit; dispatching again would send RIOT3 a second robot
    ///         for goods already on the way or delivered.</item>
    /// </list>
    /// </summary>
    internal static GroupAttempt NextAttempt(
        IReadOnlyList<TripAttemptRef> priorTrips, Guid deliveryOrderId, int groupIndex)
    {
        TripAttemptRef? tip = null;
        var tipAttempt = 0;
        foreach (var trip in priorTrips)
        {
            if (!EnvelopeUpperKey.TryParse(trip.UpperKey, out var orderId, out var g, out var parsedAttempt)
                || orderId != deliveryOrderId || g != groupIndex)
                continue;

            var attempt = Math.Max(parsedAttempt, trip.AttemptNumber);
            if (attempt > tipAttempt)
            {
                tip = trip;
                tipAttempt = attempt;
            }
        }

        if (tip is null) return new GroupAttempt(false, 1, null);

        var ended = tip.Status is TripStatus.Failed or TripStatus.Cancelled or TripStatus.Rejected;
        return ended
            ? new GroupAttempt(false, tipAttempt + 1, tip.Id)
            : new GroupAttempt(true, tipAttempt, tip.PreviousAttemptId, tip);
    }
}
