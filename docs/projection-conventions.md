# DTMS — Event Projection Conventions

**Status:** P0 foundation — applies to every projector built from P1 onwards.

This document codifies how DTMS implements **Event Projection** on top of its
existing **Outbox + MassTransit + RabbitMQ** event-driven foundation. Every
projector added under the Event Projection plan MUST follow these conventions
so observability, replay, and idempotency stay uniform across the codebase.

---

## 1. Vocabulary

| Term | Meaning |
|---|---|
| **Event** | Immutable past-tense fact (`DeliveryOrderCancelledIntegrationEvent`) |
| **Projector** | Consumer that materializes a read model row from one or more events |
| **Read model** | Denormalized table optimized for a specific query pattern |
| **Source of truth** | Aggregate write model — never the projection |
| **Replay** | Re-process historical events to rebuild a read model from scratch |
| **Inbox** | Bookkeeping table that tracks which `EventId` a projector has processed |
| **Projector lag** | `now - event.OccurredOn` for the most recent processed event |

---

## 2. Hard Rules (Non-Negotiable)

1. **Idempotent** — Re-delivering the same event MUST be a no-op.
   - Enforce via `EventId` unique constraint on read-model PK **or** inherit
     from `IdempotentProjector<TEvent>` which tracks processed events in a
     per-DbContext inbox table.
2. **Single writer per read model** — Only one projector class writes to a
   given read model table. Other consumers may read but never write.
3. **No back-calls to write side** — Events MUST carry every field the
   projector needs. If a field is missing, enrich the event, do not query
   back to the aggregate.
4. **No side effects** — Projectors do not send email, call external APIs,
   or dispatch commands. Side-effect consumers are separate classes.
5. **Deterministic** — Given the same event stream input, the read model
   output is identical regardless of replay count, order of retries, or
   instance count.
6. **Read model is derived** — Deleting a read model table is recoverable
   via replay. Operators must be able to do this without data loss.

---

## 3. Naming Conventions

| Artifact | Convention | Example |
|---|---|---|
| Projector class | `{ReadModel}Projector` | `OrderStatusHistoryProjector` |
| Read model table | `{schema}.{aggregate}_{purpose}` | `deliveryorder.order_status_history` |
| Read model entity | `{ReadModel}Row` | `OrderStatusHistoryRow` |
| Read model repository | `I{ReadModel}Repository` | `IOrderStatusHistoryRepository` |
| Inbox table | `{schema}.projection_inbox` | `deliveryorder.projection_inbox` |
| Query endpoint | `GET /api/v1/{aggregate}/{id}/{purpose}` | `/api/v1/delivery-orders/{id}/status-history` |
| Metric prefix | `dtms.projection.*` | `dtms.projection.lag_seconds` |

---

## 4. Implementation Checklist for a New Projector

When adding a new projector, complete every item:

- [ ] Inherit `IdempotentProjector<TEvent>` (or implement equivalent dedup).
- [ ] Read model table includes `event_id uuid UNIQUE NOT NULL`.
- [ ] Read model table includes `occurred_at timestamptz NOT NULL` (use event
      time, not processing time).
- [ ] Index supporting the primary query pattern.
- [ ] Migration with descriptive name (`{date}_Add{ReadModel}Table.cs`).
- [ ] Unit test: handler maps event to row correctly.
- [ ] Unit test: duplicate event is a no-op (idempotency).
- [ ] Unit test: out-of-order event is handled (use `OccurredOn` not arrival).
- [ ] Lag metric is recorded via `ProjectionMetrics.RecordLag(projectorName, evt.OccurredOn)`.
- [ ] Backfill SQL (or programmatic backfill) seeds existing aggregates.
- [ ] Query handler + endpoint sit on the read model, not the write model.
- [ ] Documented in `docs/projector-catalog.md`.

---

## 5. Idempotency Strategy

Two acceptable strategies — pick one per projector and stay consistent.

### 5.1 Strategy A — Inbox table (preferred for projectors with side rows)

Used when the projector writes ≥ 1 row per event. The base class
`IdempotentProjector<TEvent>` records the `EventId` in a per-module
`projection_inbox` table inside the same transaction as the read-model
write. Re-deliveries find the inbox row and short-circuit.

```csharp
public class OrderStatusHistoryProjector : IdempotentProjector<DeliveryOrderConfirmedIntegrationEventV1>
{
    protected override async Task ProjectAsync(
        DeliveryOrderConfirmedIntegrationEventV1 evt,
        CancellationToken ct)
    {
        // Project — caller already de-duplicated.
        await _repo.InsertAsync(new OrderStatusHistoryRow { ... });
    }
}
```

### 5.2 Strategy B — UNIQUE constraint on `event_id`

Used when the projector's natural write is `INSERT ... ON CONFLICT DO NOTHING`.
Simpler but assumes exactly-one row per event.

```sql
CREATE UNIQUE INDEX ux_order_history_event_id
    ON deliveryorder.order_status_history(event_id);
```

---

## 6. Event Ordering

Per-aggregate ordering is REQUIRED when a projector cares about transition
direction (e.g. status history `FromStatus` derived from previous row).

### 6.1 Bus-level

MassTransit endpoints SHOULD use `Exchange Routing` with the aggregate id
as routing key when the projector is order-sensitive. (P0 ships with default
ordering — projectors that need stricter ordering opt in at endpoint config.)

### 6.2 Application-level guard

Always compare `event.OccurredOn` to the latest row already projected for
the aggregate. If the new event is older, do not overwrite.

```csharp
var latest = await _repo.GetLatestForAggregate(orderId, ct);
if (latest != null && evt.OccurredOn < latest.OccurredAt)
{
    _logger.LogWarning("Out-of-order event {EventId} skipped for {OrderId}", evt.EventId, orderId);
    return;
}
```

---

## 7. Failure Handling

- **Transient (DB unavailable, lock timeout):** Throw — MassTransit retries
  per outbox retry policy (5 attempts, exponential 30s → 2h).
- **Permanent (malformed event, schema mismatch):** Log + swallow — do not
  block the queue. Future: route to DLQ for inspection.
- **Concurrency conflict:** `DbUpdateConcurrencyException` → throw, will be
  retried; idempotency makes the retry safe.

---

## 8. Observability

Every projector MUST emit:

- `dtms.projection.events_projected_total{projector, event_type}` (counter)
- `dtms.projection.lag_seconds{projector}` (histogram — event time to
  processing time)
- `dtms.projection.dedup_skipped_total{projector}` (counter — when inbox hit)

Failures MUST log with structured fields: `projector`, `event_id`,
`aggregate_id`, `error`.

---

## 9. Replay Contract

Every projector MUST be safe to replay across any window. The
`IProjectionReplayService` (P0.4) provides the orchestration; projectors
just need to be idempotent and ordering-safe (sections 5 & 6).

```bash
# Future operator command
dotnet run --project DTMS.Cli -- replay \
    --projector OrderStatusHistoryProjector \
    --from 2026-01-01 \
    --aggregate-id <guid>
```

Replay is for fixing read-model drift after a projector bug, not for
backfill of pre-existing aggregates (that is a one-off SQL seed).

---

## 10. Deferred from P0 (revisit before next phase)

The following P0 items are intentionally deferred — projection layer ships
without them, but they should be reconsidered before scaling beyond P1:

| Item | Why deferred | Trigger to revisit |
|---|---|---|
| Integration-event V2 (TriggeredBy, CorrelationId fields) | V1 already carries `Reason`; adding `TriggeredBy` requires AmbientAuditContext (own initiative). | When compliance/audit pushes for actor on every row |
| Per-aggregate RabbitMQ routing | MassTransit `ConfigureEndpoints` default ordering is sufficient for P1. | When a projector reports out-of-order events in production |
| Admin projection health page (P0.F5) | Building it before there are projectors to monitor is wasted. | After P1 ships and ops requests visibility |
| Full Replay CLI tool | Interface ships, implementation later — Phase P1 doesn't depend on it. | When the first projector bug fix needs a replay |

---

## 11. References

- [Martin Fowler — What do you mean by Event-Driven?](https://martinfowler.com/articles/201701-event-driven.html)
- DTMS Outbox: [`src/AMR.DeliveryPlanning.SharedKernel/Outbox/`](../src/AMR.DeliveryPlanning.SharedKernel/Outbox/)
- DTMS Domain Events: [`src/Modules/*/Domain/Events/`](../src/Modules/)
