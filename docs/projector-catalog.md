# DTMS — Projector Catalog

**The single source of truth for every projection worker in the
system.** Read this first when:

- A `/reports` or dashboard widget shows wrong data → find the
  projector that owns the column.
- You're adding a new integration event → see which projectors
  already consume it (so you don't accidentally break a contract).
- You're refactoring a read model → see which endpoint + frontend
  page would regress.
- You're onboarding to the projection layer → walk the cards top to
  bottom.

**Companions:**
- [event-projection-plan.md](event-projection-plan.md) — what's planned, what's shipped, decision log.
- [projection-conventions.md](projection-conventions.md) — how to write a new projector (P0 patterns).

**Last updated:** 2026-06-15 — covers 11 event-driven projectors + 1
background snapshot writer across 4 modules. Update this doc whenever
you add a projector, subscribe to a new event, or change a read model
schema.

---

## Quick reference 1 — read model → projector

Use when you see a wrong value in a `bi.*` or `<schema>.*` table and
need to find the writer.

| Read model | Owner module | Projector | Backfill script | Phase |
|---|---|---|---|---|
| `deliveryorder.OrderStatusHistory` | DeliveryOrder | `OrderStatusHistoryProjector` | `backfill-p1-order-status-history.sql` | P1 |
| `deliveryorder.OrderActivity` | DeliveryOrder | `OrderActivityProjector` | `backfill-p2-order-activity.sql` | P2 |
| `deliveryorder.OrderFunnelHourly` | DeliveryOrder | `OrderFunnelProjector` | `backfill-p3-order-funnel-hourly.sql` | P3.1 |
| `deliveryorder.OrderListView` | DeliveryOrder | `OrderListViewProjector` | `backfill-p4-order-list-view.sql` | P4 |
| `bi.OrderFacts` | DeliveryOrder | `OrderFactsProjector` | `backfill-p5-order-facts.sql` | P5.1 |
| `planning.JobStatusHistory` | Planning | `JobStatusHistoryProjector` | `backfill-p1-job-status-history.sql` | P1 |
| `bi.JobFacts` | Planning | `JobFactsProjector` | `backfill-p5-job-facts.sql` | P5.2 |
| `dispatch.TripStatusHistory` | Dispatch | `TripStatusHistoryProjector` | `backfill-p1-trip-status-history.sql` | P1 |
| `bi.TripFacts` | Dispatch | `TripFactsProjector` | `backfill-p5-trip-facts.sql` | P5.2 |
| `dispatch.TripItems` | Dispatch | `TripItemsProjector` | `backfill-p5.3-trip-items.sql` | P5.3 |
| `fleet.VehicleStateHistory` | Fleet | `VehicleStateHistoryProjector` | (none — event-driven only) | P3.2 |
| `fleet.FleetUtilizationHourly` | Fleet | `FleetUtilizationSnapshotWriter` (background) | (none — periodic rebuild) | P3.2 |

Every module has its own `ProjectionInbox` table (`<schema>.ProjectionInbox`)
keyed on `(ProjectorName, EventId)` for at-least-once → effectively-once.

---

## Quick reference 2 — event → projectors that consume it

Use when you change an integration event's shape and need to know what
breaks.

### DeliveryOrder events (11)

| Integration event | Consumed by (count) |
|---|---|
| `DeliveryOrderConfirmedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderDispatchedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderInProgressIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderCompletedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderPartiallyCompletedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderFailedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderCancelledIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderRejectedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderHeldIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderReleasedIntegrationEventV1` | OrderStatusHistory, OrderActivity, OrderFunnel, OrderListView, OrderFacts (5) |
| `DeliveryOrderAmendedIntegrationEventV1` | OrderStatusHistory, OrderActivity (2) — funnel/list/facts ignore amendments |

### Dispatch (Trip) events (8)

| Integration event | Consumed by (count) |
|---|---|
| `TripStartedIntegrationEvent` (V1.2: + Items snapshot) | TripStatusHistory, TripFacts, OrderActivity, OrderListView, TripItems (5) |
| `TripPickupCompletedIntegrationEvent` | OrderActivity (1) — item-level events flow through DeliveryOrder.Items elsewhere |
| `TripDropCompletedIntegrationEvent` | OrderActivity (1) |
| `TripCompletedIntegrationEvent` | TripStatusHistory, TripFacts, OrderActivity, OrderListView, TripItems (5) |
| `TripFailedIntegrationEvent` | TripStatusHistory, TripFacts, OrderActivity, OrderListView, TripFailedJobConsumer, TripItems (6) |
| `TripCancelledIntegrationEvent` | TripStatusHistory, TripFacts, OrderActivity, OrderListView, TripCancelledJobConsumer, TripItems (6) |
| `TripPausedIntegrationEventV1` | TripStatusHistory, TripFacts, OrderActivity, TripPausedJobConsumer (4) |
| `TripResumedIntegrationEventV1` | TripStatusHistory, TripFacts, OrderActivity, TripResumedJobConsumer (4) |
| `ExceptionRaisedIntegrationEvent` | OrderActivity (1) |

### Planning (Job) events (10)

| Integration event | Consumed by (count) |
|---|---|
| `JobCreatedIntegrationEventV1` | JobStatusHistory, JobFacts, OrderListView (3) |
| `JobAssignedIntegrationEvent` | JobFacts (1) |
| `PlanCommittedIntegrationEvent` | JobStatusHistory, JobFacts (2) |
| `JobDispatchedIntegrationEventV1` | JobStatusHistory, JobFacts, OrderListView (3) |
| `JobExecutingIntegrationEventV1` | JobStatusHistory, JobFacts, OrderListView (3) |
| `JobCompletedIntegrationEventV1` | JobStatusHistory, JobFacts, OrderListView (3) |
| `JobFailedIntegrationEventV1` | JobStatusHistory, JobFacts, OrderListView (3) |
| `JobCancelledIntegrationEventV1` | JobStatusHistory, JobFacts, OrderListView (3) |
| `JobPausedIntegrationEventV1` | JobStatusHistory (1) — added Phase #1 |
| `JobResumedIntegrationEventV1` | JobStatusHistory (1) — added Phase #1 |

### Fleet events (1)

| Integration event | Consumed by (count) |
|---|---|
| `VehicleStateChangedIntegrationEvent` | VehicleStateHistory (1) |

### Cross-module consumers (non-projector — included for completeness)

These aren't projectors but they subscribe to the same events and matter
for impact assessment:

| Consumer | Subscribes to | Purpose |
|---|---|---|
| `TripStartedJobConsumer` (Planning) | `TripStartedIntegrationEvent` | `Job.MarkExecuting()` mirror |
| `TripCompletedJobConsumer` (Planning) | `TripCompletedIntegrationEvent` | `Job.MarkCompleted()` mirror |
| `TripFailedJobConsumer` (Planning) | `TripFailedIntegrationEvent` | `Job.MarkFailed()` mirror |
| `TripCancelledJobConsumer` (Planning) | `TripCancelledIntegrationEvent` | `Job.MarkCancelled()` mirror |
| `TripPausedJobConsumer` (Planning) | `TripPausedIntegrationEventV1` | `Job.MarkPaused()` mirror (Phase #1) |
| `TripResumedJobConsumer` (Planning) | `TripResumedIntegrationEventV1` | `Job.MarkResumed()` mirror (Phase #1) |
| `DeliveryOrderValidatedConsumer` (Planning) | `DeliveryOrderValidatedIntegrationEvent` | Auto-plan trigger — creates Jobs + dispatches envelopes |

---

## Per-projector cards

### 1. `OrderStatusHistoryProjector` (P1)

| | |
|---|---|
| **Module** | DeliveryOrder |
| **DbContext** | `DeliveryOrderDbContext` |
| **Read model** | `deliveryorder.OrderStatusHistory` (row per status transition; `FromStatus` chained from latest row) |
| **Subscribes** | 11 DeliveryOrder lifecycle events (Confirmed → Dispatched → InProgress → Completed/PartiallyCompleted/Failed/Cancelled/Rejected/Held/Released/Amended) |
| **Dedup** | `deliveryorder.ProjectionInbox` (ProjectorName=`OrderStatusHistoryProjector`) |
| **Downstream API** | `GET /api/v1/delivery-orders/{id}/status-history` |
| **Downstream UI** | `<StatusTimelineSection />` in Order detail drawer |
| **Backfill** | `scripts/backfill-p1-order-status-history.sql` (seeds from `OrderAuditEvents` text) |
| **Tests** | `tests/Modules/DeliveryOrder.UnitTests/OrderStatusHistoryProjectorTests.cs` |
| **Notes** | `FromStatus` is computed by reading the previous row at projection time. Out-of-order events are skipped + warning-logged (preserves chain). |

### 2. `OrderActivityProjector` (P2)

| | |
|---|---|
| **Module** | DeliveryOrder |
| **DbContext** | `DeliveryOrderDbContext` |
| **Read model** | `deliveryorder.OrderActivity` (unified per-order timeline; 1 row per event with category discriminator) |
| **Subscribes** | 11 DeliveryOrder + 8 Trip events (Started/Pickup/Drop/Completed/Failed/Cancelled/Paused/Resumed) + ExceptionRaised = 20 |
| **Dedup** | `deliveryorder.ProjectionInbox` (ProjectorName=`OrderActivityProjector`) |
| **Downstream API** | `GET /api/v1/delivery-orders/{id}/audit-full` (transparent swap of the legacy 4-source UNION query) |
| **Downstream UI** | `<FullAuditLog />` in Order detail drawer |
| **Backfill** | `scripts/backfill-p2-order-activity.sql` (seeds from `OrderAuditEvents`, `OrderAmendments`, `ExecutionEvents`, `TripRetryEvents`) |
| **Tests** | `tests/Modules/DeliveryOrder.UnitTests/OrderActivityProjectorTests.cs` |
| **Notes** | TripPaused/Resumed/ExceptionRaised events have no `DeliveryOrderId` payload — projector skips them on the live path (still seeded by backfill). P2.5 backlog: enrich events to close the gap. |

### 3. `OrderFunnelProjector` (P3.1)

| | |
|---|---|
| **Module** | DeliveryOrder |
| **DbContext** | `DeliveryOrderDbContext` |
| **Read model** | `deliveryorder.OrderFunnelHourly` (hour-bucketed wide row, one column per status — UNIQUE on `BucketHour`) |
| **Subscribes** | 10 DeliveryOrder lifecycle events (no Amended — funnel is for status transitions) |
| **Dedup** | `deliveryorder.ProjectionInbox` (ProjectorName=`OrderFunnelProjector`) |
| **Downstream API** | `GET /api/v1/dashboard/order-funnel?fromUtc=&toUtc=` |
| **Downstream UI** | `<KpiRail />` + `<DispatchFunnel />` on `/dashboard`; stacked-area chart on `/dashboard/orders` |
| **Backfill** | `scripts/backfill-p3-order-funnel-hourly.sql` (pivots `OrderStatusHistory` from P1 into hour buckets — no event replay needed) |
| **Tests** | `tests/Modules/DeliveryOrder.UnitTests/OrderFunnelProjectorTests.cs` |
| **Notes** | Each event increments exactly one column for its event-hour bucket. Window queries cap at 90 days in the handler. |

### 4. `OrderListViewProjector` (P4)

| | |
|---|---|
| **Module** | DeliveryOrder |
| **DbContext** | `DeliveryOrderDbContext` |
| **Read model** | `deliveryorder.OrderListView` (denormalized row per order — filter columns + display columns + derived booleans + `SearchText` + GENERATED `SearchVector` tsvector with GIN index) |
| **Subscribes** | 10 Order + 4 Trip + 6 Job lifecycle events = 20 |
| **Dedup** | `deliveryorder.ProjectionInbox` (ProjectorName=`OrderListViewProjector`) |
| **Downstream API** | `GET /api/v1/delivery-orders` (replaces the runtime JOIN-heavy query); supports `search`, `hasFailedTrip`, `hasActiveJob` |
| **Downstream UI** | `/delivery-orders` list page (Orders Experience) |
| **Backfill** | `scripts/backfill-p4-order-list-view.sql` (3 LATERAL JOINs to seed item-level SearchText + Trip/Job derived flags in one pass) |
| **Tests** | `tests/Modules/DeliveryOrder.UnitTests/OrderListViewProjectorTests.cs` |
| **Notes** | `HasFailedTrip` / `HasActiveJob` are "ever-true" not "currently-true" (MVP — flipping back would require cross-projection reads). `SearchText` concat'd then DB derives `SearchVector` via GENERATED ALWAYS AS to_tsvector('simple', SearchText) STORED. |

### 5. `OrderFactsProjector` (P5.1)

| | |
|---|---|
| **Module** | DeliveryOrder |
| **DbContext** | `DeliveryOrderDbContext` |
| **Read model** | `bi.OrderFacts` (wide row-per-order; dimensions + measures + 11 lifecycle timestamp columns; 5 GENERATED STORED KPIs: TimeToConfirmSec, TimeToDispatchSec, TimeToCompleteSec, SlaConfirmBreached, SlaCompleteBreached) |
| **Subscribes** | 10 DeliveryOrder lifecycle events |
| **Dedup** | `deliveryorder.ProjectionInbox` (ProjectorName=`OrderFactsProjector`) |
| **Downstream API** | `GET /api/v1/reports/orders-summary`, `/sla-breach`, `/top-failures`, `/lead-time`, `/orders-export` (CSV) |
| **Downstream UI** | `/reports` tabs: Orders by priority, SLA breach, Top failures, Lead time |
| **Backfill** | `scripts/backfill-p5-order-facts.sql` (pivots `OrderStatusHistory` from P1 — no event replay) |
| **Tests** | `tests/Modules/DeliveryOrder.UnitTests/OrderFactsProjectorTests.cs` |
| **Notes** | EF reads the GENERATED columns via `PropertySaveBehavior.Ignore` — the projector can never overwrite them. SLA thresholds (4h confirm, 24h complete) are baked into the migration's GENERATED expression. |

### 6. `JobStatusHistoryProjector` (P1, +Phase #1)

| | |
|---|---|
| **Module** | Planning |
| **DbContext** | `PlanningDbContext` |
| **Read model** | `planning.JobStatusHistory` |
| **Subscribes** | 9 events: JobCreated, PlanCommitted, JobDispatched, JobExecuting, JobCompleted, JobFailed, JobCancelled, JobPaused, JobResumed |
| **Dedup** | `planning.ProjectionInbox` (ProjectorName=`JobStatusHistoryProjector`) |
| **Downstream API** | `GET /api/v1/planning/jobs/{id}/status-history` |
| **Downstream UI** | `<StatusTimelineSection />` in Job detail drawer inside JobsExperience |
| **Backfill** | `scripts/backfill-p1-job-status-history.sql` |
| **Tests** | `tests/Modules/Planning.UnitTests/JobStatusHistoryProjectorTests.cs` |
| **Notes** | Phase #1 added JobPaused/JobResumed subscriptions. PlanCommitted (not JobCommitted) is the existing integration event for the Committed transition. |

### 7. `JobFactsProjector` (P5.2)

| | |
|---|---|
| **Module** | Planning |
| **DbContext** | `PlanningDbContext` |
| **Read model** | `bi.JobFacts` (row-per-job; dimensions + measures + lifecycle timestamps; 3 GENERATED STORED KPIs: TimeToDispatchSec, TimeToCompleteSec, SlaDispatchBreached; FailureCategory column added Phase #9) |
| **Subscribes** | 8 events: JobCreated, JobAssigned, PlanCommitted, JobDispatched, JobExecuting, JobCompleted, JobFailed, JobCancelled |
| **Dedup** | `planning.ProjectionInbox` (ProjectorName=`JobFactsProjector`) |
| **Downstream API** | `GET /api/v1/reports/job-failures` (Phase #9) |
| **Downstream UI** | `/reports` tab: "Job failures" (category breakdown) |
| **Backfill** | `scripts/backfill-p5-job-facts.sql` (sources `FailureCategory` from `planning.Jobs`) |
| **Tests** | `tests/Modules/Planning.UnitTests/JobFactsProjectorTests.cs` |
| **Notes** | `FailureCategory` (Phase #9) is a string at the wire level — projector collapses null/whitespace to `"None"` so the column never holds NULL. JobCancelled fixes category to `"OperatorCancelled"`. |

### 8. `TripStatusHistoryProjector` (P1)

| | |
|---|---|
| **Module** | Dispatch |
| **DbContext** | `DispatchDbContext` |
| **Read model** | `dispatch.TripStatusHistory` (nullable DeliveryOrderId/JobId to accommodate pause/resume payloads) |
| **Subscribes** | 6 events: TripStarted, TripPaused, TripResumed, TripCompleted, TripFailed, TripCancelled |
| **Dedup** | `dispatch.ProjectionInbox` (ProjectorName=`TripStatusHistoryProjector`) |
| **Downstream API** | `GET /api/v1/dispatch/trips/{id}/status-history` |
| **Downstream UI** | `<StatusTimelineSection />` in Trip detail drawer |
| **Backfill** | `scripts/backfill-p1-trip-status-history.sql` (seeds initial `Created` rows since `Trip.CreateForEnvelope` doesn't emit a domain event) |
| **Tests** | `tests/Modules/Dispatch.UnitTests/TripStatusHistoryProjectorTests.cs` |
| **Notes** | Pause/Resume integration events carry only `TripId` — projector carries forward DeliveryOrderId/JobId from the latest history row. |

### 9. `TripFactsProjector` (P5.2, +Phase #10)

| | |
|---|---|
| **Module** | Dispatch |
| **DbContext** | `DispatchDbContext` |
| **Read model** | `bi.TripFacts` (row-per-trip; lifecycle timestamps + PauseCount + 3 GENERATED STORED KPIs: TimeToStartSec, TimeToCompleteSec, SlaCompleteBreached; VendorVehicleKey column added Phase #10) |
| **Subscribes** | 6 events: TripStarted, TripPaused, TripResumed, TripCompleted, TripFailed, TripCancelled |
| **Dedup** | `dispatch.ProjectionInbox` (ProjectorName=`TripFactsProjector`) |
| **Downstream API** | `GET /api/v1/reports/vehicle-performance`, `/trips-export` (CSV) |
| **Downstream UI** | `/reports` tab: "Vehicle performance" (group by `VendorVehicleKey`) |
| **Backfill** | `scripts/backfill-p5-trip-facts.sql` (sources `VendorVehicleKey` from `dispatch.Trips`) |
| **Tests** | `tests/Modules/Dispatch.UnitTests/TripFactsProjectorTests.cs` |
| **Notes** | No `TripCreated` integration event exists — `EnsureRowAsync` lazily creates the fact row on the first inbound event. Backfill SQL seeds true `CreatedAt` from `dispatch.Trips` for pre-P5.2 rows. |

### 10. `VehicleStateHistoryProjector` (P3.2)

| | |
|---|---|
| **Module** | Fleet |
| **DbContext** | `FleetDbContext` |
| **Read model** | `fleet.VehicleStateHistory` (one row per VehicleState transition with FromState derived from the prior row) |
| **Subscribes** | 1 event: `VehicleStateChangedIntegrationEvent` |
| **Dedup** | `fleet.ProjectionInbox` (ProjectorName=`VehicleStateHistoryProjector`) |
| **Downstream API** | (none direct — consumed by `FleetUtilizationSnapshotWriter` below) |
| **Downstream UI** | Vehicle detail drawer history section |
| **Backfill** | (none — Fleet has no pre-projection historical data to seed) |
| **Tests** | `tests/Modules/Fleet.UnitTests/VehicleStateHistoryProjectorTests.cs` |
| **Notes** | Only single-event projector in the system. FromState carry-forward + out-of-order skip mirror P1 patterns. |

### 11. `TripItemsProjector` (P5.3)

| | |
|---|---|
| **Module** | Dispatch |
| **DbContext** | `DispatchDbContext` |
| **Read model** | `dispatch.TripItems` (row per (TripId, ItemPk); denormalized — embeds OrderRef/OrderStatus snapshot + lot details for one-shot operator drawer query) |
| **Subscribes** | 4 events: TripStarted (insert), TripCompleted (→ItemStatus="Delivered"), TripFailed/TripCancelled (→ItemStatus="Unbound") |
| **Dedup** | `dispatch.ProjectionInbox` (ProjectorName=`TripItemsProjector`) |
| **Downstream API** | `GET /api/v1/dispatch/trips/{id}/items` |
| **Downstream UI** | `<TripItemsSection />` in Trip detail drawer (compact table; clickable OrderRef opens Order drawer on top) |
| **Backfill** | `scripts/backfill-p5.3-trip-items.sql` (cross-schema join — Trips × Items × DeliveryOrders) |
| **Tests** | `tests/Modules/Dispatch.UnitTests/TripItemsProjectorTests.cs` |
| **Notes** | Requires `TripStartedIntegrationEvent.Items` (V1.2 enrichment) — populated by `ITripItemSnapshotProvider` (impl: `DeliveryOrderTripItemSnapshotProvider` in DeliveryOrder.Infrastructure) before `Trip.MarkVendorStarted` is called. OrderRef/OrderStatus are snapshotted at trip-start and **not** refreshed by Order lifecycle events — operator can re-fetch order state via OrderId for live status. Empty `Items` payload is valid: projector records the inbox row to prevent reprocessing and waits for a future enrichment event. |

### 12. `FleetUtilizationSnapshotWriter` (P3.2 — *background, not event-driven*)

| | |
|---|---|
| **Module** | Fleet (writer); hosted by Api project |
| **DbContext** | `FleetDbContext` |
| **Read model** | `fleet.FleetUtilizationHourly` (vehicle state distribution per hour bucket — UPSERT on current bucket) |
| **Trigger** | `FleetUtilizationSnapshotService` (hosted background service, 60s tick, 20s warmup delay) |
| **Dedup** | N/A — UPSERT on `BucketHour` PK |
| **Downstream API** | `GET /api/v1/dashboard/fleet-utilization` |
| **Downstream UI** | `<FleetUtilizationChart />` on `/dashboard/robots` |
| **Backfill** | (none — periodic rebuild fills past 24h automatically; older buckets stay as captured) |
| **Tests** | (none — thin SQL wrapper; covered indirectly by the snapshot service smoke tests) |
| **Notes** | Hybrid pattern — event-driven `VehicleStateHistory` feeds the state machine, periodic snapshot writer materializes the aggregate. Same approach is the canonical answer when raw events are too noisy for a dashboard column. |

---

## Observability

Every event-driven projector emits OpenTelemetry metrics via
`ProjectionMetrics` (singleton, meter name `DTMS.Projection`):

| Metric | Tags | Use |
|---|---|---|
| `dtms.projection.events_projected_total` | projector, event_type | throughput per projector |
| `dtms.projection.dedup_skipped_total` | projector, event_type | duplicate webhook rate |
| `dtms.projection.permanent_failures_total` | projector, event_type | schema-drift / null-deref crashes |
| `dtms.projection.lag_seconds` | projector | event-time → projection-time histogram |

In-app view: [/admin/projections](/admin/projections) (CC4 — shipped
2026-06-13, commit `8528a077`) shows per-projector last-processed time,
processed count, and health status (healthy < 5 min, stale < 1 h,
idle ≥ 1 h).

---

## When to add a new projector

1. Read [projection-conventions.md](projection-conventions.md) for the
   P0 patterns (IdempotentProjector helpers, store + read repo split,
   per-module DbContext, DI shape).
2. Add per-module concrete `IProjectionStore` and `IReadRepository` —
   never share interfaces across modules (DI conflict).
3. Create the migration + backfill script together; backfill SQL pivots
   from `*StatusHistory` (P1) whenever possible — avoids event replay.
4. Tests: 1 file per projector under `tests/Modules/<Module>.UnitTests/`;
   cover happy path per event, dedup, out-of-order skip, permanent vs
   transient failure split.
5. **Update this doc** — add to both quick-reference tables + a new
   per-projector card.
6. Add to [event-projection-plan.md](event-projection-plan.md) decision
   log if there's a non-obvious choice (e.g. column type, GENERATED
   expression, cross-module event shape).
