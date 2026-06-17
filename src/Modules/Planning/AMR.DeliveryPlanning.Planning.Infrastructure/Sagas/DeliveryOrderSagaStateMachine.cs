using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Sagas;

/// <summary>
/// T2 POC — Saga state machine that will eventually replace the procedural
/// <c>DeliveryOrderValidatedConsumer</c>. The advantage over the consumer is
/// that state is persisted to <c>orchestration.DeliveryOrderSagas</c> on every
/// transition, so a pod crash mid-workflow doesn't lose progress — the next
/// pod (or the same one after restart) resumes from the saved state.
///
/// <para><b>POC scope</b>: only the initial transition
/// (<see cref="OrderConfirmed"/> → AwaitingPlan) is implemented. Every other
/// transition lands in Phase 2 along with compensation handlers and timeouts.
/// The full state diagram is in
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

        // Redeliveries of the OrderConfirmed event on an already-started saga
        // are no-op: the state machine only matches the Initially() block on
        // the first hit, and we don't declare a During(AwaitingPlan)
        // handler so subsequent deliveries are silently ignored. That's the
        // idempotency story T1.5 had to write into command handlers by hand.
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
