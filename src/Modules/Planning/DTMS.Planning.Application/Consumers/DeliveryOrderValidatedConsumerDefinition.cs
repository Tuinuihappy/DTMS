using MassTransit;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

// Phase A Step A5 — concurrency tuning for the heaviest consumer in the
// auto-pipeline. MassTransit's auto-configuration (cfg.ConfigureEndpoints
// in ModuleServiceRegistration) discovers ConsumerDefinition<T> classes
// alongside consumers and applies them, so this file is the right hook
// for per-consumer endpoint tuning without removing the auto-config setup.
//
// Why this consumer specifically:
//   The k6 acceptance runs across 2026-06-20 converged on one finding —
//   outbox drain is bottlenecked by *consumer-side* throughput, not by
//   outbox publish (Phase A A1-A4) or connection pool (Phase B) or
//   container CPU (Phase D). Under the load test profile (NoOp vendor),
//   DeliveryOrderValidatedConsumer is the ONLY consumer firing heavy
//   DB work — it owns the entire auto-plan pipeline:
//     MarkOrderPlanning → CreateJobAnchor (per group) → MarkOrderPlanned
//     → DispatchByRoute (NoOp) → MarkJobDispatched → MarkOrderDispatched
//   That's ~5+ DB ops per order. With ConcurrentMessageLimit=1 (default),
//   a single consumer instance processes one order at a time = ~20 ord/s
//   per instance. Measured aggregate drain across the system was ~67-78/s
//   in yesterday's runs — consistent with 1 consumer × 1 concurrency
//   bottlenecked on per-order serialization.
//
// Why 4 (not 8):
//   Phase D split means consumers run in BOTH dtms-api and dtms-outbox-worker
//   containers. RabbitMQ's competing-consumer behaviour distributes messages
//   between them, so per-container=4 → effective ~8 parallel handlers across
//   the system. Higher than 4-per-container amplifies Postgres row-lock
//   contention on the same DeliveryOrders.Status UPDATEs without adding
//   throughput. 4 is the largest value where added concurrency reliably
//   converts to drain rate, per the row-lock contention model.
//
// Why nothing for PrefetchCount here:
//   The global cfg.PrefetchCount = 16 set in ModuleServiceRegistration
//   gives each endpoint a 16-message in-memory buffer ready to feed
//   the 4 concurrent handlers — 4× headroom, sufficient. Raising further
//   per-endpoint requires overriding ConfigureConsumer; not worth the
//   added surface until measurement shows starvation.
public sealed class DeliveryOrderValidatedConsumerDefinition
    : ConsumerDefinition<DeliveryOrderValidatedConsumer>
{
    public DeliveryOrderValidatedConsumerDefinition()
    {
        ConcurrentMessageLimit = 4;
    }
}
