# DTMS — Projection Replay Runbook

**Status:** P0 foundation — operational guide for replaying a projector
when its read model has drifted from the event stream.

This runbook covers the **what / when / how** for ops. Architectural
context lives in [projection-conventions.md](projection-conventions.md);
the strategic plan is in [event-projection-implementation-plan.md](event-projection-implementation-plan.md).

---

## 1. What is replay?

Replay re-feeds historical integration events through a projector's
`Consume` method so its read model can be rebuilt — partially or
completely — from authoritative event data.

```
Outbox / event archive  ──[selected by (projector, time range, aggregate)]──▶
                         ▶ Projector.Consume(evt)
                            ├─ Inbox check (already processed? → skip)
                            ├─ Write read-model row(s)
                            └─ Push to SignalR group (best-effort)
```

Inbox deduplication is what makes replay safe: a re-fed event whose
`EventId` already exists in `ProjectionInbox` short-circuits without
writing anything. **Replay is therefore idempotent by construction** —
running it twice on the same range is equivalent to running it once.

---

## 2. When to replay

| Scenario | Replay needed? | Notes |
|---|---|---|
| Bug fix in projector mapping logic | ✅ Yes — full window where bug was active | Delete affected rows first if mapping was wrong (not just incomplete) |
| New projector added retroactively | ✅ Yes — from earliest event time | Backfill SQL is often faster; see §6 |
| Read model rows accidentally deleted | ✅ Yes — full window | Inbox still has the records, so first delete the matching inbox rows |
| Single aggregate's projection out of sync | ✅ Targeted — pass `aggregateId` | Fastest; only the one aggregate's events replay |
| Projector lag spike but no logic bug | ❌ No | Wait for outbox processor to catch up; or scale-out |
| Permanent projector failure (poison event) | 🟡 Depends | Fix the projector first; replay after deploy |
| Inbox table corruption | ⚠️ Manual | Truncate inbox + replay full history; see §7 |

---

## 3. Today's reality (P0)

The replay service registered in DI is `NotImplementedReplayService`:

```csharp
// SharedKernel/Projection/IProjectionReplayService.cs
public sealed class NotImplementedReplayService : IProjectionReplayService
{
    public Task<ReplaySummary> ReplayAsync(...) =>
        throw new NotImplementedException(
            "Projection replay is not yet implemented — see projection-conventions.md §10.");
}
```

This means:
- `POST /api/v1/admin/projections/{name}/replay` returns **501 Not Implemented**
- The `<ReplayDialog />` in `/admin/projections` surfaces the message verbatim
- The **contract + UI are validated**, so when the real service ships
  there's no UI work to do

**Manual replay** procedure for the interim period is documented in §5.

---

## 4. Replay via API (future-state)

Once `IProjectionReplayService` has a real implementation, the runbook
becomes a one-liner:

### 4.1 From the UI

1. Sign in to `/admin/projections`
2. Find the projector card showing the stale lag / wrong status
3. Click **Replay**
4. Pick the time window (defaults to last 24 h)
5. Optionally enter an aggregate id for a targeted replay
6. Click **Review** → confirm → **Replay now**
7. After success the page auto-refreshes; lag should drop within a few seconds

### 4.2 From cURL

```bash
TOKEN="<admin JWT>"
PROJECTOR="OrderStatusHistoryProjector"

curl -X POST "http://localhost:5219/api/v1/admin/projections/$PROJECTOR/replay" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fromUtc": "2026-06-13T00:00:00Z",
    "toUtc":   "2026-06-14T00:00:00Z"
  }'
```

Response on success:

```json
{
  "projectorName": "OrderStatusHistoryProjector",
  "fromUtc": "2026-06-13T00:00:00Z",
  "toUtc":   "2026-06-14T00:00:00Z",
  "eventsProcessed": 1247,
  "eventsSkipped": 53,
  "eventsFailed": 0,
  "elapsed": "00:00:08.124"
}
```

---

## 5. Manual replay (interim procedure)

Until `IProjectionReplayService` ships, replay is done by hand at the
SQL + outbox level. **Use only with on-call lead approval.**

### 5.1 Identify what to replay

```sql
-- Confirm the projector exists + see its last processed event
SELECT
  "ProjectorName",
  COUNT(*) AS processed,
  MAX("ProcessedAtUtc") AS last_processed
FROM deliveryorder."ProjectionInbox"
WHERE "ProjectorName" = 'OrderStatusHistoryProjector'
GROUP BY "ProjectorName";
```

### 5.2 Clear the inbox window (so events get re-processed)

```sql
BEGIN;

-- For a full rebuild within a window:
DELETE FROM deliveryorder."ProjectionInbox"
WHERE "ProjectorName" = 'OrderStatusHistoryProjector'
  AND "ProcessedAtUtc" >= '2026-06-13 00:00:00+00'
  AND "ProcessedAtUtc" <  '2026-06-14 00:00:00+00';

-- For a targeted aggregate, also delete the read-model rows the bad
-- mapping produced (otherwise the re-run produces duplicates with new
-- ids — depends on PK strategy):
DELETE FROM deliveryorder."order_status_history"
WHERE "OrderId" = '<aggregateId>'
  AND "OccurredAtUtc" >= '<from>'
  AND "OccurredAtUtc" <  '<to>';

-- Verify counts look right; if so:
COMMIT;
-- otherwise:
ROLLBACK;
```

### 5.3 Re-emit events from the outbox

The OutboxProcessorService picks up unprocessed messages every 5 s.
If you need to force re-publish without waiting for natural traffic:

```sql
-- Reset OutboxMessage status for the window (only if archived rows
-- are still in the table; otherwise the original publish was definitive
-- and replay needs to come from cold storage).
UPDATE outbox."OutboxMessages"
SET "ProcessedOnUtc" = NULL,
    "Error" = NULL,
    "RetryCount" = 0,
    "NextRetryAtUtc" = NULL
WHERE "OccurredOnUtc" >= '2026-06-13 00:00:00+00'
  AND "OccurredOnUtc" <  '2026-06-14 00:00:00+00'
  AND "Type" LIKE '%DeliveryOrderCancelledIntegrationEvent%';
```

The OutboxProcessor will re-publish on its next poll (≤ 5 s). The
RabbitMQ queue routes to the projector's consumer, the inbox check
finds no record (we deleted it), and the row gets re-projected.

### 5.4 Verify

```sql
-- Inbox rebuilt
SELECT COUNT(*) FROM deliveryorder."ProjectionInbox"
WHERE "ProjectorName" = 'OrderStatusHistoryProjector'
  AND "ProcessedAtUtc" >= '2026-06-13 00:00:00+00';

-- Read model rows in the window
SELECT COUNT(*) FROM deliveryorder."order_status_history"
WHERE "OccurredAtUtc" >= '2026-06-13 00:00:00+00'
  AND "OccurredAtUtc" <  '2026-06-14 00:00:00+00';
```

Open `/admin/projections` — lag for the projector should be near zero;
the per-module event count goes up.

---

## 6. Backfill vs replay

| | Backfill | Replay |
|---|---|---|
| Source | Current state of write tables | Historical event stream |
| Tool | One-off SQL script | `IProjectionReplayService` (or §5 manual) |
| When | Onboarding a brand-new projector retroactively | Fixing drift after a projector bug |
| Speed | Fast (one INSERT...SELECT per module) | Slow (one consume call per event) |
| Idempotent? | Only if SQL has `ON CONFLICT DO NOTHING` | Yes, via inbox |
| Picks up actor/reason | No (current snapshot has no event-time context) | Yes (events carry `TriggeredBy`/`Reason`) |

Practical rule: **backfill the historical baseline, replay for incremental
correctness.** The b12 Status History phase did exactly this — backfill SQL
seeded the initial rows, projector takes over from then.

---

## 7. Disaster recovery — full inbox rebuild

When the inbox table itself is corrupted (extremely rare; would require
a manual SQL accident or a partition drop):

1. Stop the API (or set the projector's queue to paused)
2. Truncate the affected module's inbox: `TRUNCATE deliveryorder."ProjectionInbox";`
3. Delete every read-model row the projector owns
4. Restart the API → outbox processor + bus republish everything (assumes
   outbox retention covers the full history)

If outbox retention doesn't cover full history, recovery requires an
**event archive** in cold storage (S3 / MinIO) — see Phase P6 in the
implementation plan.

---

## 8. Operational signals to watch during replay

| Signal | Where | What "healthy" looks like |
|---|---|---|
| Projector lag | `/admin/projections` | Drops toward zero within seconds of replay completion |
| `dtms.projection.events_projected_total{projector}` | OpenTelemetry → Grafana | Counter ticks up |
| `dtms.projection.dedup_skipped_total{projector}` | Same | Ticks up too (re-fed events that already exist) |
| `dtms.projection.lag_seconds{projector}` (histogram) | Same | Tail drops back to baseline (≤ 5 s) |
| RabbitMQ queue depth | rabbitmq management UI | Spikes briefly, drains within minutes |
| Postgres write IO | DB monitoring | Spike during replay; should not impact write transactions |

---

## 9. Pitfalls (don't do these)

| ❌ | Why it's bad |
|---|---|
| Replay during peak write window | Doubles the load on the read model schema; serialize-write contention |
| Replay without first deleting the bad read-model rows | If the projector overwrites by EventId, fine; if it appends by row PK, you get duplicates |
| Run multiple replays in parallel | Each fights for the inbox UNIQUE constraint; degenerate to single throughput anyway |
| Replay without first noting the original lag baseline | You lose the "did this actually fix anything" evidence |
| Skip the `/admin/projections` confirmation before running | Trivial typo on projector name = wasted hours |

---

## 10. Cross-references

- Convention rules → [projection-conventions.md](projection-conventions.md)
- Hub catalog → [signalr-hub-catalog.md](signalr-hub-catalog.md)
- Plan + decisions → [event-projection-implementation-plan.md](event-projection-implementation-plan.md)
- Projector list → [projector-catalog.md](projector-catalog.md)
