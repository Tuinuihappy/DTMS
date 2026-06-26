using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReplanStuckOrder;

/// <summary>
/// T1.7 — admin recovery for orders stuck after a partial workflow failure.
/// Unlike <see cref="RedispatchDeliveryOrder.RedispatchDeliveryOrderCommand"/>
/// which requires Status=Confirmed (operator must /reopen first), this
/// command accepts an order at any in-flight status (Confirmed / Planning /
/// Planned / Dispatched) and re-publishes the integration event the Planning
/// consumer subscribes to. Safe under replay because of the idempotency
/// guards added in T1.5 (CreateJobAnchor, MarkJobDispatched are no-ops on
/// matching second runs).
///
/// <para>Also used by <c>PlanningReconciliationService</c> (T1.4 watchdog)
/// so the manual and automatic paths share one implementation.</para>
/// </summary>
public record ReplanStuckOrderCommand(
    Guid OrderId,
    string TriggeredBy,
    string Reason,
    bool RequireStuckPlanned = false) : ICommand<ReplanStuckOrderResult>;

public record ReplanStuckOrderResult(
    Guid OrderId,
    string PreviousStatus,
    int ItemCount,
    DateTime PublishedAtUtc);
