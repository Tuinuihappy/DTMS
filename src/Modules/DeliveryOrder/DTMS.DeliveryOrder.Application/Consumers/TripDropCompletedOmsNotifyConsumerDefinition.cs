using MassTransit;

namespace DTMS.DeliveryOrder.Application.Consumers;

// Serializes the OMS /arrived notify endpoint so the dedup guard in
// TripDropCompletedOmsNotifyConsumer is (near-)race-free. RIOT3 re-emits
// SUB_TASK_FINISHED — and self-managed sources re-POST /source/trips/{id}/drop
// — several times per trip, so this consumer fires repeatedly for the same
// shipment. The consumer skips a duplicate /arrived by checking for an
// existing UpstreamOmsArrivedNotified audit row (IOrderAuditEventRepository
// .ExistsAsync). That check is read-then-write, so two drop events for the
// SAME shipment processed concurrently could both pass the check before either
// commits its audit, and double-send.
//
// ConcurrentMessageLimit = 1 forces one-at-a-time processing per endpoint
// instance: the first send commits its audit before the next message is
// dequeued, so the second sees the marker and skips. MassTransit auto-applies
// this definition when the consumer's assembly is scanned by bus.AddConsumers
// (ModuleServiceRegistration), overriding the global ConfigureEndpoints
// concurrency for this endpoint only.
//
// Caveats (why this narrows the race rather than proving exactly-once):
//   • Phase D runs consumers in BOTH dtms-api and dtms-outbox-worker. With
//     competing consumers, limit=1 per process ⇒ effective 2 across the
//     system, so two drops for one shipment landing on different containers
//     at the same instant can still overlap. In practice a trip's drops
//     arrive seconds apart and get picked up sequentially; the audit check is
//     the backstop. A DB unique constraint would be the only truly race-free
//     guarantee — deferred as overkill for the observed volume.
//   • In-process UseMessageRetry holds the single slot during its backoff, so
//     an OMS outage can head-of-line-block other arrived notifies for up to
//     ~1 minute before the message drops to delayed (re-queued) redelivery.
//     Acceptable: /arrived is low-volume and correctness outweighs the stall.
public sealed class TripDropCompletedOmsNotifyConsumerDefinition
    : ConsumerDefinition<TripDropCompletedOmsNotifyConsumer>
{
    public TripDropCompletedOmsNotifyConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }
}
