namespace AMR.DeliveryPlanning.Planning.Infrastructure.Sagas;

/// <summary>
/// T2 — explicit state of the order-to-trip orchestration. Stored on every
/// <see cref="DeliveryOrderSagaInstance"/> row so a restart can resume from
/// the persisted state instead of relying on broker redelivery. Values are
/// stable so safely persisted as int columns; new states must append, never
/// reorder.
///
/// <para>POC scope: only the happy-path states are reachable from event
/// handlers. <see cref="FailedAwaitingRetry"/> is declared but not yet
/// targeted — that's a Phase 2 (compensation) addition.</para>
/// </summary>
public enum OrderSagaState
{
    None = 0,                  // implicit — saga row doesn't exist yet
    AwaitingPlan = 1,          // entered on DeliveryOrderConfirmedIntegrationEventV1
    Planning = 2,              // job anchor + groups being built
    Dispatching = 3,           // vendor dispatch in flight
    Completed = 4,             // every group reached vendor; terminal
    FailedAwaitingRetry = 5,   // any in-flight state hit a fault or timeout
}
