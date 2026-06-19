using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Sagas;

/// <summary>
/// T2 Phase 2 step 1 — Saga state machine that will eventually replace the
/// procedural <c>DeliveryOrderValidatedConsumer</c>. The advantage over the
/// consumer is that state is persisted to <c>orchestration.DeliveryOrderSagas</c>
/// on every transition, so a pod crash mid-workflow doesn't lose progress —
/// the next pod (or the same one after restart) resumes from the saved state.
///
/// <para><b>Step 1 scope</b>:
///   • <see cref="OrderConfirmed"/> → AwaitingPlan (POC, already verified)
///   • <see cref="PlanRequested"/> → Planning (NEW — first transition beyond
///     AwaitingPlan, published by the legacy <c>DeliveryOrderValidatedConsumer</c>
///     after <c>MarkOrderPlanning</c> so dual-run shadow mode works)
///   • <c>OrderConfirmed</c> redeliveries explicitly ignored in every user
///     state (NEW — was the worst POC finding: without these, T1.4 watchdog
///     redeliveries of the same event throw <c>NotAcceptedStateMachineException</c>)
/// </para>
///
/// <para>Every other transition lands in later Phase 2 steps along with
/// compensation handlers and timeouts. The full target state diagram is in
/// <c>docs/crash-recovery-workflow-resilience-plan.md</c> section 3.1.</para>
///
/// <para><b>Disabled by default</b>. Activated by setting
/// <c>Workflow:UseSaga=true</c>. While disabled the state machine and its
/// queue exist but no events are routed to it — production traffic still
/// flows through the legacy consumer (T1 path).</para>
/// </summary>
public sealed class DeliveryOrderSagaStateMachine : MassTransitStateMachine<DeliveryOrderSagaInstance>
{
    public DeliveryOrderSagaStateMachine(ILogger<DeliveryOrderSagaStateMachine> logger)
    {
        // Bind the integer column to the State property the state machine reads.
        // MassTransit converts state names ↔ int via the OrderSagaState enum below.
        InstanceState(x => x.CurrentState,
            AwaitingPlan, Planning, Dispatching, Completed, FailedAwaitingRetry);

        // Route every event by the order id it carries. The DeliveryOrder
        // module's integration events all expose DeliveryOrderId, which we use
        // as the saga CorrelationId — one saga per order, ever.
        Event(() => OrderConfirmed,
            e => e.CorrelateById(ctx => ctx.Message.DeliveryOrderId));

        // Initially → AwaitingPlan. The Saga row is created on first hit.
        Initially(
            When(OrderConfirmed)
                .Then(ctx =>
                {
                    ctx.Saga.UpdatedAtUtc = DateTime.UtcNow;
                    logger.LogInformation(
                        "[Saga] Order {OrderId} entered AwaitingPlan via OrderConfirmed (eventId {EventId})",
                        ctx.Saga.CorrelationId, ctx.Message.EventId);
                })
                .TransitionTo(AwaitingPlan));

        // Step 1 — redelivery dedup. MassTransit's default for an event on a
        // saga in a state with no matching handler is to throw
        // NotAcceptedStateMachineException — the message then enters the
        // retry/DLQ path and reads as a system fault. In production every
        // redelivery source (T1.1 MassTransit retry, T1.4 watchdog,
        // T1.7 /admin/orders/{id}/replan) republishes the same
        // DeliveryOrderConfirmed event for an order whose saga has already
        // moved past AwaitingPlan, so without these handlers the saga is
        // unusable. Explicit Ignore() makes the redelivery a silent no-op.
        During(AwaitingPlan,         Ignore(OrderConfirmed));
        During(Planning,             Ignore(OrderConfirmed));
        During(Dispatching,          Ignore(OrderConfirmed));
        During(Completed,            Ignore(OrderConfirmed));
        During(FailedAwaitingRetry,  Ignore(OrderConfirmed));
    }

    // States — each must be a public property so MassTransit's reflection can
    // bind them to int values. State name = property name.
    public State AwaitingPlan { get; private set; } = null!;
    public State Planning { get; private set; } = null!;
    public State Dispatching { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State FailedAwaitingRetry { get; private set; } = null!;

    // Events — same pattern. The Event<T> is bound in the ctor with the
    // correlation policy.
    public Event<DeliveryOrderConfirmedIntegrationEventV1> OrderConfirmed { get; private set; } = null!;
}
