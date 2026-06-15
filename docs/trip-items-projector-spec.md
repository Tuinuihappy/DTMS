# Spec — `TripItemsProjector` (proposed)

**Status:** Draft / not implemented. Targeting Phase **P5.3** of the
event-projection plan. Owner: Dispatch module.

**Problem it solves**
- No current endpoint answers "given a `TripId`, return all items
  on that trip + the order each item belongs to" in one call.
- Workarounds today: client must call `GET /api/v1/dispatch/orders/{orderId}/trips`
  → then `GET /api/v1/delivery-orders/{id}/items` → then client-side
  filter on `tripId`. N+1 over `orderId`. Operator UI doesn't have
  the inverse direction at all.
- Direct cross-context query (Dispatch joins `deliveryorder.Items`)
  violates bounded-context boundary and breaks if the modules ever
  split.

**Why a projection (not a BFF aggregator)**
- Pattern matches the 10 existing projectors — uses the same
  `IdempotentProjector` base, `dispatch.ProjectionInbox`, replay
  endpoint, and OTel metrics that operators already know.
- Item ↔ Trip binding is stable once a trip starts (vendor doesn't
  re-bind mid-flight), so the read model rarely churns after the
  first `TripStarted` event.
- Eventual consistency lag (< 100 ms in current load) is acceptable
  for an operator drill-down view — same SLA as `OrderListView`.

---

## 1. Read model — `dispatch.TripItems`

Row per (TripId, ItemPk). Denormalized for one-shot query; same
shape as the existing `dispatch.TripStatusHistory` (lives in the
Dispatch schema because the query is Trip-driven).

```sql
CREATE TABLE dispatch."TripItems" (
    "TripId"          uuid        NOT NULL,
    "ItemPk"          uuid        NOT NULL,                  -- deliveryorder.Items.Id
    "EventId"         uuid        NOT NULL,                  -- last event that wrote this row (UNIQUE for replay)
    "DeliveryOrderId" uuid        NOT NULL,
    "OrderRef"        text        NOT NULL,                  -- snapshot at trip-start
    "OrderStatus"     text        NOT NULL,                  -- snapshot; refreshed by Order lifecycle events
    "LotNo"           text        NOT NULL,                  -- Items.ItemId (the lot string, e.g. "LOT-01KV021QHF")
    "ItemSeq"         int         NOT NULL,
    "ItemStatus"      text        NOT NULL,                  -- snapshot; refreshed by Pickup/Drop events
    "PickupCode"      text,
    "DropCode"        text,
    "WeightKg"        double precision,
    "BoundAt"         timestamptz NOT NULL,                  -- when item joined this trip (= TripStarted.OccurredOn)
    "LastEventAt"     timestamptz NOT NULL,                  -- last refresh
    CONSTRAINT "PK_TripItems" PRIMARY KEY ("TripId", "ItemPk"),
    CONSTRAINT "UX_TripItems_EventId" UNIQUE ("EventId")     -- dedup safety net
);

CREATE INDEX "IX_TripItems_OrderId" ON dispatch."TripItems" ("DeliveryOrderId");
CREATE INDEX "IX_TripItems_OrderRef" ON dispatch."TripItems" ("OrderRef");
CREATE INDEX "IX_TripItems_LotNo" ON dispatch."TripItems" ("LotNo");
```

**Naming note:** the table is plural like sibling read models
(`TripStatusHistory`, `OrderListView`). `EventId` is `UNIQUE` not
`PRIMARY KEY` because a single event refreshes N rows.

---

## 2. Integration event changes required

The Hard Rule from `projection-conventions.md §2.3` — *"Events MUST
carry every field the projector needs"* — forces these enrichments
**before the projector can be implemented**. Each is backward-compat
(nullable, defaulted) so existing consumers are unaffected.

### 2.1 Enrich `TripStartedIntegrationEvent` (Dispatch)

```csharp
public record TripStartedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid JobId, Guid VehicleId,
    Guid DeliveryOrderId,
    string? VendorVehicleKey = null,
    string? TriggeredBy = null,
    Guid? CorrelationId = null,
    // NEW — items bound to the trip at start. Empty list is valid
    // (vendor adapter may bind items later). Snapshot fields are
    // populated by DispatchDomainEventMapper from order + items.
    IReadOnlyList<TripItemSnapshot>? Items = null
) : IIntegrationEvent;

public record TripItemSnapshot(
    Guid ItemPk,
    int ItemSeq,
    string LotNo,
    string ItemStatus,
    string PickupCode,
    string DropCode,
    double? WeightKg,
    Guid DeliveryOrderId,
    string OrderRef,
    string OrderStatus);
```

**Why on `TripStarted`** — that's the only Trip event that already
materializes "this trip now exists with its items locked in".
`TripCreated` doesn't have an integration event (the existing
`TripStatusHistoryProjector` notes this and seeds via backfill —
same problem applies here).

**Impact map** (from `projector-catalog.md` row 4):
- Existing consumers: `TripStatusHistory`, `TripFacts`,
  `OrderActivity`, `OrderListView`, plus the cross-module
  `TripStartedJobConsumer`. All four projectors ignore unknown
  fields (default ctor params). Verify on the consumer-side smoke
  tests — no migration needed on those tables.

### 2.2 Optional: `ItemPodScannedIntegrationEvent` (DeliveryOrder)

Only needed if `ItemStatus` column on `TripItems` should reflect
per-item POD progress (Picked / Dropped). If MVP is fine with
"status as of trip-start", skip this and add it in a follow-up.

```csharp
public record ItemPodScannedIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid ItemPk, Guid TripId, Guid DeliveryOrderId,
    string ScanType,           // "Pickup" | "Drop"
    string NewItemStatus       // e.g. "PickedUp", "Delivered"
) : IIntegrationEvent;
```

Emit from `ItemPodScanCommandHandler` (already raises a domain event
internally — needs outbox mapping).

### 2.3 Existing events the projector also subscribes to

No payload changes needed — these refresh denormalized fields:

| Event | Field refreshed on `TripItems` |
|---|---|
| `DeliveryOrderCompletedIntegrationEventV1` | `OrderStatus = "Completed"` (all rows for that order) |
| `DeliveryOrderCancelledIntegrationEventV1` | `OrderStatus = "Cancelled"` |
| `TripCompletedIntegrationEvent` | `ItemStatus` for any still-`Bound` rows on the trip → `"Delivered"` |
| `TripCancelledIntegrationEvent` | `ItemStatus → "Unbound"` |
| `TripFailedIntegrationEvent` | `ItemStatus → "Unbound"` |

---

## 3. Projector skeleton (C#)

File: `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Projections/TripItemsProjector.cs`

```csharp
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Phase P5.3 — Materializes (Trip, Item) bindings into
/// dispatch.TripItems so a single GET answers "what items are on
/// this trip and which order owns each one?".
///
/// Row lifecycle:
///   TripStarted             → INSERT N rows (one per snapshot item)
///   TripCompleted/Failed/   → UPDATE ItemStatus for rows of that trip
///     Cancelled
///   OrderCompleted/Cancelled→ UPDATE OrderStatus for rows of that order
///   ItemPodScanned (opt.)   → UPDATE ItemStatus per row
///
/// Idempotent + out-of-order safe (P1 pattern). Inbox key:
/// (ProjectorName=TripItemsProjector, EventId).
/// </summary>
public class TripItemsProjector :
    IConsumer<TripStartedIntegrationEvent>,
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>,
    IConsumer<DeliveryOrderCompletedIntegrationEventV1>,
    IConsumer<DeliveryOrderCancelledIntegrationEventV1>
    // + IConsumer<ItemPodScannedIntegrationEvent> if §2.2 is included
{
    public const string Name = nameof(TripItemsProjector);

    private readonly ITripItemsProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<TripItemsProjector> _logger;

    public TripItemsProjector(
        ITripItemsProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<TripItemsProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    // --- TripStarted: insert rows -------------------------------------

    public async Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
    {
        var evt = ctx.Message;
        if (await DedupAsync(evt.EventId, nameof(TripStartedIntegrationEvent), ctx.CancellationToken)) return;

        var items = evt.Items ?? Array.Empty<TripItemSnapshot>();
        if (items.Count == 0)
        {
            // Vendor adapter binds items later — projector waits for a
            // future enrichment event. Record the EventId so we don't
            // re-process and log so operators can spot stuck trips.
            await _store.RecordEmptyBindingAsync(Name, evt.EventId, evt.TripId, evt.OccurredOn, ctx.CancellationToken);
            _logger.LogInformation("Trip {TripId} started with no item snapshot — row deferred", evt.TripId);
            return;
        }

        await _store.InsertBindingsAsync(
            Name, evt.EventId, evt.TripId, evt.OccurredOn,
            items, ctx.CancellationToken);

        _metrics.RecordProjected(Name, nameof(TripStartedIntegrationEvent));
        _metrics.RecordLag(Name, evt.OccurredOn);
    }

    // --- Trip terminal: refresh ItemStatus on rows of this trip ------

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => UpdateItemStatusForTripAsync(ctx, ctx.Message.TripId, "Delivered");

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => UpdateItemStatusForTripAsync(ctx, ctx.Message.TripId, "Unbound");

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => UpdateItemStatusForTripAsync(ctx, ctx.Message.TripId, "Unbound");

    // --- Order terminal: refresh OrderStatus on rows of this order ---

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => UpdateOrderStatusAsync(ctx, ctx.Message.OrderId, "Completed");

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => UpdateOrderStatusAsync(ctx, ctx.Message.OrderId, "Cancelled");

    // --- helpers ------------------------------------------------------

    private async Task UpdateItemStatusForTripAsync<TEvent>(
        ConsumeContext<TEvent> ctx, Guid tripId, string newStatus)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        if (await DedupAsync(evt.EventId, typeof(TEvent).Name, ctx.CancellationToken)) return;

        var updated = await _store.UpdateItemStatusByTripAsync(
            Name, evt.EventId, tripId, newStatus, evt.OccurredOn, ctx.CancellationToken);

        _metrics.RecordProjected(Name, typeof(TEvent).Name);
        _metrics.RecordLag(Name, evt.OccurredOn);
        _logger.LogInformation(
            "Trip {TripId} → ItemStatus={NewStatus} ({UpdatedCount} rows)",
            tripId, newStatus, updated);
    }

    private async Task UpdateOrderStatusAsync<TEvent>(
        ConsumeContext<TEvent> ctx, Guid orderId, string newStatus)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        if (await DedupAsync(evt.EventId, typeof(TEvent).Name, ctx.CancellationToken)) return;

        var updated = await _store.UpdateOrderStatusByOrderAsync(
            Name, evt.EventId, orderId, newStatus, evt.OccurredOn, ctx.CancellationToken);

        _metrics.RecordProjected(Name, typeof(TEvent).Name);
        _metrics.RecordLag(Name, evt.OccurredOn);
    }

    private async Task<bool> DedupAsync(Guid eventId, string eventType, CancellationToken ct)
    {
        if (await _store.HasProcessedEventAsync(Name, eventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, eventType);
            return true;
        }
        return false;
    }
}
```

---

## 4. Store + read-repo contract

Split write-store (used by projector) from read-repo (used by query
handler) — same pattern as every other projector in the system.

```csharp
// src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/
//   Projections/Stores/ITripItemsProjectionStore.cs
public interface ITripItemsProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct);

    Task InsertBindingsAsync(
        string projectorName, Guid eventId,
        Guid tripId, DateTime occurredAt,
        IReadOnlyList<TripItemSnapshot> items,
        CancellationToken ct);

    Task RecordEmptyBindingAsync(
        string projectorName, Guid eventId,
        Guid tripId, DateTime occurredAt, CancellationToken ct);

    Task<int> UpdateItemStatusByTripAsync(
        string projectorName, Guid eventId,
        Guid tripId, string newStatus, DateTime occurredAt,
        CancellationToken ct);

    Task<int> UpdateOrderStatusByOrderAsync(
        string projectorName, Guid eventId,
        Guid deliveryOrderId, string newStatus, DateTime occurredAt,
        CancellationToken ct);
}

// src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Repositories/ITripItemsRepository.cs
public interface ITripItemsRepository
{
    Task<IReadOnlyList<TripItemRow>> GetByTripAsync(Guid tripId, CancellationToken ct);
    Task<IReadOnlyList<TripItemRow>> GetByOrderAsync(Guid orderId, CancellationToken ct);
}

public sealed record TripItemRow(
    Guid TripId, Guid ItemPk,
    Guid DeliveryOrderId, string OrderRef, string OrderStatus,
    string LotNo, int ItemSeq, string ItemStatus,
    string? PickupCode, string? DropCode, double? WeightKg,
    DateTime BoundAt, DateTime LastEventAt);
```

EF implementation lives under
`src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Projections/`.

---

## 5. Query + endpoint

```csharp
// src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Queries/GetTripItems/
public record GetTripItemsQuery(Guid TripId) : IQuery<TripItemsResponse>;

public sealed record TripItemsResponse(
    Guid TripId, int ItemCount, IReadOnlyList<TripItemDto> Items);

public sealed record TripItemDto(
    Guid ItemPk, string LotNo, int ItemSeq, string ItemStatus,
    string? PickupCode, string? DropCode, double? WeightKg,
    OrderRefDto Order, DateTime BoundAt);

public sealed record OrderRefDto(Guid Id, string OrderRef, string Status);
```

Endpoint registration in `DispatchEndpoints.cs` (after the existing
`/trips/{id}/details` route — same auth, same group):

```csharp
// GET /api/v1/dispatch/trips/{id}/items — Phase P5.3
// Lists every item bound to a trip plus its owning order's context.
// Backed by dispatch.TripItems read model (see TripItemsProjector).
group.MapGet("/trips/{id:guid}/items", async (Guid id, ISender sender) =>
{
    var result = await sender.Send(new GetTripItemsQuery(id));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
});
```

Sample response:

```json
{
  "tripId": "46479d0f-b92b-4c22-911b-32a4edf76c9b",
  "itemCount": 1,
  "items": [{
    "itemPk": "fdd7bc4f-2207-4a6e-a7da-22aacfe765e3",
    "lotNo": "LOT-01KV021QHF",
    "itemSeq": 1,
    "itemStatus": "Bound",
    "pickupCode": "ST-PICKUP-A",
    "dropCode": "ST-DROP-B",
    "weightKg": 500,
    "order": {
      "id": "af386363-b50b-4fc6-bfc2-67016a77eee9",
      "orderRef": "OD-0338-WIP",
      "status": "Dispatched"
    },
    "boundAt": "2026-06-15T01:52:09Z"
  }]
}
```

---

## 6. Migration + backfill

Migration:
`src/AMR.DeliveryPlanning.Api/Migrations/Dispatch/{date}_AddTripItemsReadModel.cs`
— creates the table + indices from §1 above. Manual hand-write (per
`feedback_migration_manual.md` — `dotnet-ef` doesn't work on .NET 10
preview).

Backfill:
`scripts/backfill-p5.3-trip-items.sql` — single INSERT … SELECT joining
the write-side tables to seed every existing trip (no event replay
needed):

```sql
INSERT INTO dispatch."TripItems"
    ("TripId", "ItemPk", "EventId", "DeliveryOrderId", "OrderRef",
     "OrderStatus", "LotNo", "ItemSeq", "ItemStatus",
     "PickupCode", "DropCode", "WeightKg", "BoundAt", "LastEventAt")
SELECT
    t."Id"                    AS "TripId",
    i."Id"                    AS "ItemPk",
    gen_random_uuid()         AS "EventId",          -- synthetic; never collides with live events
    o."Id"                    AS "DeliveryOrderId",
    o."OrderRef",
    o."Status"                AS "OrderStatus",
    i."ItemId"                AS "LotNo",
    i."ItemSeq",
    i."Status"                AS "ItemStatus",
    i."PickupLocationCode"    AS "PickupCode",
    i."DropLocationCode"      AS "DropCode",
    i."WeightKg",
    COALESCE(t."StartedAt", t."CreatedAt") AS "BoundAt",
    now() AT TIME ZONE 'UTC'  AS "LastEventAt"
FROM dispatch."Trips" t
JOIN deliveryorder."Items" i ON i."TripId" = t."Id"
JOIN deliveryorder."DeliveryOrders" o ON o."Id" = t."DeliveryOrderId"
ON CONFLICT ("TripId", "ItemPk") DO NOTHING;
```

`ON CONFLICT DO NOTHING` makes the backfill re-runnable. Cross-schema
JOIN is fine here — backfill SQL is a one-shot operator script, not
runtime code.

---

## 7. Tests checklist

File: `tests/Modules/Dispatch.UnitTests/TripItemsProjectorTests.cs`

- [ ] `TripStarted` with N items → N rows inserted, `BoundAt` = `OccurredOn`
- [ ] `TripStarted` with empty Items list → no item rows, inbox row written
- [ ] Duplicate `TripStarted` (same EventId) → second delivery is a no-op
- [ ] `TripCompleted` → all `Bound` rows of that trip flip to `Delivered`
- [ ] `TripFailed` / `TripCancelled` → all rows flip to `Unbound`
- [ ] `DeliveryOrderCompleted` for order with rows on 2 trips → both refresh
- [ ] Out-of-order event (OccurredOn < last write) → skipped, warning logged
- [ ] Permanent failure (e.g. null deref on bad event) → metric recorded, event dropped (no retry storm)

---

## 8. `projector-catalog.md` — new entry

Add a row to "Quick reference 1" table:

| `dispatch.TripItems` | Dispatch | `TripItemsProjector` | `backfill-p5.3-trip-items.sql` | P5.3 |

Add to "Quick reference 2" — Dispatch events section:

| `TripStartedIntegrationEvent` | TripStatusHistory, TripFacts, OrderActivity, OrderListView, **TripItems** (5) |

And add per-projector card #12 mirroring the §1-7 above.

---

## 9. Rollout sequence

1. PR #1 — Schema enrichment of `TripStartedIntegrationEvent` (§2.1)
   alone. Adds `TripItemSnapshot` payload, updates
   `DispatchDomainEventMapper` to populate from order+items. Existing
   consumers unaffected (nullable default).
2. PR #2 — `TripItemsProjector` + read model migration + repo +
   endpoint + tests. Ship behind a feature flag (`Projections__TripItems__Enabled`)
   if there's any concern about double-writes.
3. PR #3 — Backfill SQL. Run in staging, verify row count matches
   `SELECT COUNT(DISTINCT (TripId, Id)) FROM Trips JOIN Items …`,
   then run in prod off-hours.
4. PR #4 (optional) — `ItemPodScannedIntegrationEvent` enrichment
   (§2.2) so `ItemStatus` reflects POD progress in real-time.
5. PR #5 — Wire the new endpoint into the operator UI trip drawer.

---

## 10. Open questions

- **Should the projector live in Dispatch or DeliveryOrder?** Spec
  puts it in Dispatch because the query is `GET /trips/{id}/items`.
  The alternative (DeliveryOrder schema) would put it next to
  `OrderListView` but violates "query-driven schema placement".
- **`OrderRef` snapshot vs join?** Spec snapshots into `TripItems`.
  Means: if Order is amended after dispatch, the read model goes
  stale until the next event refreshes it. Acceptable because
  OrderRef is treated as immutable post-confirm in this codebase
  (per [project_order_ref_identity.md](../memory/project_order_ref_identity.md)).
- **Empty `Items` on `TripStarted`** — current vendor adapter
  binds items at envelope-build time (before `TripStarted`), so the
  list should never be empty in practice. The empty-list branch in
  the projector is defensive; if telemetry confirms it never fires
  after 30 days, remove that branch.
