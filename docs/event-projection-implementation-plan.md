# DTMS Event Projection + SignalR — Final Implementation Plan

> **Document status:** Phase P0 + P1 shipped (2026-06-15); P2-P5 pending
> **Decision date:** 2026-06-14
> **Scope:** Project-wide rollout of Event Projection pattern with realtime SignalR push
> **Stack:** .NET 10 + ASP.NET SignalR + MessagePack + Redis backplane + Next.js 16

---

## Phase status

| Phase | Scope | Status |
|---|---|---|
| **P0 Foundation** | IdempotentProjector base, ActorContext, event V1.1 enrichment, 5 SignalR hubs, MessagePack + JWT-in-query + CORS, filters (Tracing + RateLimit), throttlers (DashboardCounterBatcher 250ms / FleetPositionThrottler 1s), `/admin/projections` page + ReplayDialog, runbooks | ✅ Done (Days 1-7 / 2026-06-14) |
| **P1 Status History** | 3 publisher abstractions (Order/Job/Trip), 3 SignalR-backed impls, projectors push `TimelineUpdated` after row insert, 3 detail drawers wired via `useXxxSubscription` hooks, `<StatusTimelineSection liveEntry>` dedup-merge — **first feature on realtime SignalR**, E2E verified | ✅ Done (2026-06-15) |
| **P2 Activity Timeline** | `OrderActivityProjector` extended (+Created/Submitted/Validated/RobotPassAck/PodCaptured = 5 events), `IOrderRealtimePublisher.PublishActivityUpdatedAsync` wires push to `OrderHub.ActivityUpdated`, `OrderActivityRow.Id = EventId` (deterministic for dedup), `FullAuditLog` gets `liveEntry` prop with dedup-merge sort, `StatusTimelineSection` retired from Order drawer (Option A). E2E verified — cancel order → activity row written + push silent. | ✅ Done (2026-06-16) |
| **P3 Dashboard Read Models — increment 1 (orders board)** | `IDashboardRealtimePublisher.PublishOrderFunnelUpdatedAsync` abstraction in DeliveryOrder.Application; `BatchedDashboardRealtimePublisher` in Api forwards hints to existing `DashboardCounterBatcher` (P0.B11) — coalesces into 1 `CountersUpdated` per board per 250 ms; `OrderFunnelProjector` enqueues hint after `IncrementAsync`; `/dashboard/orders` page (orders-analysis-experience.tsx) subscribes via `useDashboardSubscription("orders")` and debounce-refetches (500 ms) — no client-side delta merge to avoid chart drift. E2E verified — cancel order → funnel cancelled 1→2 with REST refresh hint delivered. | ✅ Done (2026-06-16) |
| **P3.x Dashboard increments 2-3 (KPI rail + fleet board)** | `FleetUtilizationSnapshotService` (Api/Infrastructure) injects `DashboardCounterBatcher` directly (already in Api composition root) and enqueues `"fleet"` hint after every successful `UpsertCurrentBucketAsync`; `RobotsAnalysisExperience` (`/dashboard/robots`) subscribes via `useDashboardSubscription("fleet")` + 500ms debounce refresh. `KpiRail` on `/dashboard` overview reuses the existing `"orders"` board (same `OrderFunnel` data source) — no new backend wiring, just frontend subscribe. Pattern identical to increment 1 — proves the publisher abstraction stays optional when the producer lives in the composition root. | ✅ Done (2026-06-16) |
| **P4 Search/List** | `order_list_view` denormalized + FTS + faceted filters (`hasFailedTrip`/`hasActiveJob`) already in place by previous work; this increment adds the **live wire**: `OrderHub.SubscribeList()`/`UnsubscribeList()` for the cross-order `orders-list` group, `IOrderClient.ListItemUpdated(hint)`, `IOrderRealtimePublisher.PublishOrderListChangedAsync(orderId, changeHint)`, `SignalROrderRealtimePublisher` impl, `OrderListViewProjector.Run()` extended to fire the push after every successful store+MarkProcessed (23 events: 13 order lifecycle + 4 trip + 6 job — `changeHint` is the lifecycle status for orders, `"TripXxx"`/`"JobXxx"` for derived-field updates). Frontend: `useOrderListSubscription` hook in `lib/realtime/hubs/order-hub.ts`, `orders-experience.tsx` subscribes + debounce-refetches list+stats (500ms) — hint-and-refetch (not delta merge) so server-side FTS/facets stay authoritative. | ✅ Done (2026-06-16) |
| **P5 Reporting/BI** | Discovery during this milestone revealed P5 was shipped incrementally during earlier phases — `bi.OrderFacts` / `bi.TripFacts` / `bi.JobFacts` (36/29/34 rows) materialized by `OrderFactsProjector` / `TripFactsProjector` / `JobFactsProjector`; 6 report templates live under `/reports` (orders-summary, sla-breach, top-failures, lead-time, job-failures, vehicle-performance) backed by `ReportsEndpoints` / `DispatchReportsEndpoints` / `PlanningReportsEndpoints`; CSV exports work for orders + trips. **This increment closes the only real gap**: added `/api/v1/reports/jobs-export` (mirrors trips-export but reads `bi.JobFacts` directly) + `jobsExportCsvUrl` frontend helper; `job-failures-report.tsx` no longer hijacks the trips CSV (wrong schema for analysts). Deferred: F3 ReportBuilder flexible UI, F5 scheduled reports, F6 embed mode, B5/B6 bi-reader role + read replica (dev DB has one role only). | ✅ Done (2026-06-16) |
| **P6 Compliance** | Tamper-evidence + archival (only if regulated) | ⛔ Skipped (2026-06-16) — no regulatory requirement on the table (no GDPR/PDPA/SOX/HIPAA/ISO-27001 obligation, no EU/UK customer, no auditor ask). Existing event log + projection lineage is sufficient for operational debugging. Revisit only if a concrete compliance ask lands (customer contract, regulator inquiry, certification target). See §8 for what would need to be built when that happens. |

---

## 0. ภาพรวม Executive Summary

### Vision
ทุก read model ใน DTMS ที่ optimize สำหรับ query pattern หนึ่งๆ จะ derive จาก event stream ผ่าน **projector** + realtime push ผ่าน **SignalR** — โดยใช้ infrastructure ที่มีอยู่แล้ว 100% (Outbox/MassTransit/Redis)

### Outcomes
| สิ่งที่ผู้ใช้จะเห็น | ผลลัพธ์ |
|---|---|
| Dashboard load time | < 200ms (จาก 1-3s) |
| Order list query | < 50ms (จาก 500ms+ ที่ 100k orders) |
| Status timeline | Realtime push < 500ms latency |
| "Order entered Planning เมื่อไหร่" | Structured query, indexable |
| Audit timeline UX | Category filter + severity color + CSV export |
| Live order tracking | Push-based updates ไม่ poll |
| BI/Reports | Pre-aggregated, ไม่ contention กับ write DB |

### Scope
**6 phases (P0-P6)** + **4 cross-cutting workstreams (CC1-CC4)** — ทุก phase ship ได้แบบ independent

### Stack decisions (locked)
| Concern | Choice | Locked because |
|---|---|---|
| Realtime transport | **SignalR + MessagePack + WebSocket** | .NET-native, auto-fallback, 30-60% bandwidth saving |
| Backplane | **Redis (existing dtms-redis)** | Stack มีอยู่แล้ว, zero new infra |
| Projection storage | **Per-module PostgreSQL schemas** | Existing pattern, atomic transactions |
| Outbox / event bus | **MassTransit + RabbitMQ** | Already deployed |
| Serialization | **MessagePack + LZ4** | Binary, faster, smaller |
| Frontend updates | **SignalR push (primary) + REST snapshot (fallback)** | Best UX, graceful degradation |

---

## 1. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ User's Browser (Next.js 16)                                          │
│ ┌──────────────────────────────────────────────────────────────────┐ │
│ │ Hub clients (lazy, per page)                                      │ │
│ │ ┌─────────┬─────────┬─────────┬───────────┬─────────┐            │ │
│ │ │OrderHub │ JobHub  │ TripHub │DashboardHub│FleetHub │            │ │
│ │ └─────────┴─────────┴─────────┴───────────┴─────────┘            │ │
│ │  • @microsoft/signalr + MessagePack protocol                      │ │
│ │  • Auto-reconnect with exponential backoff                        │ │
│ │  • useHubSubscription hook (shared)                               │ │
│ │  • REST snapshot + hub deltas (hybrid)                            │ │
│ └──────────────────────────────────────────────────────────────────┘ │
└────────────────────┬─────────────────────────────────────────────────┘
                     │ WebSocket (skip negotiation) + JWT in query
                     │ REST fetch (initial snapshot, cacheable)
┌────────────────────▼─────────────────────────────────────────────────┐
│ Nginx                                                                │
│ - WebSocket upgrade + HTTP/2                                         │
│ - No buffering for streaming                                         │
└────────────────────┬─────────────────────────────────────────────────┘
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
┌─────────────┐ ┌─────────────┐ ┌─────────────┐
│ API inst 1  │ │ API inst 2  │ │ API inst N  │ ← scale-out ready
│             │ │             │ │             │
│ Hubs (5)    │ │ Hubs (5)    │ │ Hubs (5)    │
│ Projectors  │ │ Projectors  │ │ Projectors  │
│ Outbox proc │ │ Outbox proc │ │ Outbox proc │
│ Read API    │ │ Read API    │ │ Read API    │
└──────┬──────┘ └──────┬──────┘ └──────┬──────┘
       │               │               │
       └───────────────┼───────────────┘
                       ▼
        ┌────────────────────────────┐
        │ Redis (dtms-redis)         │ ← existing, zero new infra
        │ • SignalR backplane        │
        │ • Cache layer              │
        └────────────────────────────┘

        ┌────────────────────────────┐
        │ RabbitMQ (dtms-rabbitmq)   │ ← existing
        │ • Integration event bus    │
        └────────────────────────────┘

        ┌────────────────────────────┐
        │ PostgreSQL (dtms-postgres) │ ← existing
        │ • Write models             │
        │ • Read models (new)        │
        │ • Outbox tables            │
        └────────────────────────────┘
```

### Data flow (status change → user sees update)

```
1. User clicks "Cancel" → POST /orders/{id}/cancel (existing)
2. CancelOrderCommandHandler runs
3. order.Cancel(reason) → emit DeliveryOrderCancelledDomainEvent
4. DomainEventOutboxInterceptor → write OutboxMessage (same DB transaction)
5. SaveChanges commits
6. OutboxProcessor poll (every 5s) → publish to RabbitMQ
7. OrderStatusHistoryProjector (consumer) picks up
   ├─ Write row to order_status_history (read model)
   └─ Push via IHubContext to "order:{id}" group
8. SignalR backplane (Redis) routes to instance holding subscriber
9. Browser receives TimelineUpdated event
10. UI animates new entry into timeline

Total: 5-10s (outbox poll dominates) — can reduce to <1s by switching to push outbox in Stage 3
```

### Principles (enforce via convention + tests)
1. **Projector = single writer** ต่อ read model
2. **Read model = derived** — สามารถ rebuild ได้เสมอ (deterministic)
3. **Idempotency mandatory** — every projector checks `EventId` uniqueness
4. **Hub = thin** — subscription management only, no business logic
5. **Push over poll** — REST for snapshot, SignalR for deltas
6. **No DB calls in hub methods**
7. **Self-contained events** — projector doesn't fetch from write side
8. **One hub per concern** — independent scale + fault isolation

---

## 2. Phase P0 — Foundation (SignalR + Projection Framework)

**Duration:** 5-7 วัน (1 week+)
**Goal:** ทุก projector + UI component หลังจากนี้ใช้ shared infra ได้ทันที

### 2.1 Backend Deliverables

| # | Deliverable | File location | Effort |
|---|---|---|---|
| P0.B1 | `IdempotentProjector<TEvent>` base class | `SharedKernel/Projection/IdempotentProjector.cs` | XS |
| P0.B2 | `IProcessedEventTracker` + per-aggregate Postgres impl | `SharedKernel/Projection/` | S |
| P0.B3 | Event enrichment — เพิ่ม `string? TriggeredBy`, `Guid? CorrelationId` ใน status integration events (V2 alongside V1) | per-module `IntegrationEvents/` | S |
| P0.B4 | `ICurrentActorContext` + middleware (`X-User-Id` / JWT claim) | `SharedKernel/Auth/` | XS |
| P0.B5 | `ProjectionMetrics` (OpenTelemetry) — lag, throughput, errors | `SharedKernel/Projection/Observability/` | S |
| P0.B6 | `IProjectionReplayService` + CLI command | `Api/Infrastructure/Replay/` + `cli` project | M |
| P0.B7 | Per-aggregate routing key in `DomainEventMapper`s (queue ordering) | 4 mappers updated | S |
| P0.B8 | **SignalR core setup** — services + MessagePack + Redis backplane (deferred-activated) + JWT auth | `Program.cs` + `Api/Realtime/` | S |
| P0.B9 | **5 Hub classes** — OrderHub, JobHub, TripHub, DashboardHub, FleetHub | `Api/Realtime/Hubs/` | S |
| P0.B10 | **Hub filters** — `TracingHubFilter`, `RateLimitedHubFilter` (token bucket) | `Api/Realtime/Filters/` | S |
| P0.B11 | **Throttling/batching services** — `FleetPositionThrottler`, `DashboardCounterBatcher` | `Api/Realtime/Pipeline/` | M |
| P0.B12 | Health endpoint extension — projection lag, hub connection count | `Api/HealthChecks/` | XS |
| P0.B13 | `docs/projection-conventions.md` — naming, idempotency, testing pattern | `docs/` | S |

### 2.2 Frontend Deliverables

| # | Deliverable | File location | Effort |
|---|---|---|---|
| P0.F1 | Install `@microsoft/signalr` + `@microsoft/signalr-protocol-msgpack` | `package.json` | XS |
| P0.F2 | Hub connection singleton manager — `getHub(path)` with lazy init | `frontend/lib/realtime/signalr-client.ts` | S |
| P0.F3 | `useHubSubscription<T>` hook — generic subscribe + reconnect-resume | `frontend/lib/hooks/use-hub-subscription.ts` | S |
| P0.F4 | Hub typed clients (5) — OrderHub, JobHub, etc. wrapper functions | `frontend/lib/realtime/hubs/` | S |
| P0.F5 | `<DataFreshnessChip />` — "live / stale Xs ago / disconnected" | `frontend/components/realtime/` | XS |
| P0.F6 | `<ConnectionIndicator />` — small dot showing hub state | `frontend/components/realtime/` | XS |
| P0.F7 | `<ProjectionLagBanner />` — top banner when projection lag exceeds threshold | `frontend/components/realtime/` | XS |
| P0.F8 | `<TimelineView />` — vertical timeline reusable component (used P1, P2) | `frontend/components/realtime/timeline-view.tsx` | M |
| P0.F9 | `useProjectionPoll` (REST fallback for non-realtime contexts) | `frontend/lib/hooks/` | XS |
| P0.F10 | `/admin/projections` page — projection health dashboard | `frontend/app/admin/projections/` | M |
| P0.F11 | `<ReplayDialog />` — trigger replay with confirm | `frontend/components/admin/` | S |
| P0.F12 | Left-rail "Admin" → Projection health link | `frontend/components/shell/left-rail.tsx` | XS |
| P0.F13 | Design tokens — `--color-category-status`, `--color-lag-fresh`, etc. | `frontend/app/globals.css` | XS |

### 2.3 Acceptance Criteria

- ✅ Replay command rebuilds a read model from event archive (smoke test on demo aggregate)
- ✅ Hub connection establishes < 1s, MessagePack payload < 50% size of JSON equivalent
- ✅ `/admin/projections` page shows live lag for each projector
- ✅ Disconnect-reconnect test: subscribed group resumes within 3s with state intact
- ✅ Rate limiter rejects 101st invocation per second per connection with `HubException`
- ✅ All shared components (TimelineView, FreshnessChip) ready to import in P1

### 2.4 P0 Total Effort: **5-7 วัน** (single dev)

---

## 3. Phase P1 — Status History Projection (เดิม b12)

**Duration:** 5-7 วัน
**Goal:** Structured timeline ของ Order/Job/Trip — ตอบ "entered status X เมื่อไหร่" ใน <5ms

### 3.1 Backend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P1.B1 | 3 read model tables + migrations: `deliveryorder.order_status_history`, `planning.job_status_history`, `dispatch.trip_status_history` | Unique on `event_id`, index `(aggregate_id, occurred_at DESC)`, partial index on terminal status |
| P1.B2 | 3 Projector classes inheriting `IdempotentProjector<T>` | Subscribe ~8-12 events each per aggregate |
| P1.B3 | Inject `IHubContext<OrderHub, IOrderClient>` etc. → push `TimelineUpdated` after INSERT | Fire-and-forget pattern |
| P1.B4 | 3 Query handlers — `GetOrderStatusHistoryQuery` etc. (paginated) | Pagination + filter by status |
| P1.B5 | 3 REST endpoints — `GET /api/v1/{aggregate}/{id}/status-history` | Returns initial snapshot |
| P1.B6 | Backfill SQL script — seed history from current state | `scripts/p1-backfill-status-history.sql` |
| P1.B7 | Unit tests — projector idempotency, mapping correctness | xUnit + NSubstitute |
| P1.B8 | Integration tests — end-to-end event → table → hub push | Testcontainers Postgres + RabbitMQ |

### 3.2 Frontend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P1.F1 | API client functions + proxy routes (3 ตัว) | Match existing pattern |
| P1.F2 | `<StatusTimelineSection />` — uses `<TimelineView />` from P0.F8 | Reusable across drawers |
| P1.F3 | Integrate into **Order detail drawer** — section before Items | `frontend/components/delivery-orders/detail-drawer.tsx` |
| P1.F4 | Integrate into **Trip detail drawer** | `frontend/components/dispatch/trip-detail-drawer.tsx` |
| P1.F5 | Integrate into **Job detail drawer** (จาก b10-frontend.2) | `frontend/components/planning/jobs-experience.tsx` |
| P1.F6 | Filter chips — by ToStatus | inline ใน StatusTimelineSection |
| P1.F7 | Hover popover — show TriggeredBy + Reason details | inline |
| P1.F8 | Live indicator + animate new entries via SignalR | uses P0.F6 |

### 3.3 Acceptance
- ✅ Query "Order entered Planning at X" returns < 5ms
- ✅ Replay rebuilds history identically (deterministic test)
- ✅ Status change → UI update < 1s (end-to-end with SignalR)
- ✅ Backfill handles 100k aggregates without write-side blocking
- ✅ Mobile responsive

### 3.4 P1 Total Effort: **5-7 วัน**

---

## 4. Phase P2 — Activity Timeline Projection

**Duration:** 4-5 วัน
**Goal:** Unified activity stream per order — replaces existing `audit-full` UNION query

### 4.1 Backend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P2.B1 | `deliveryorder.order_activity_timeline` table | Categorized (StatusChange/Amendment/TripEvent/OmsNotify/Pod), JSONB details |
| P2.B2 | `OrderActivityProjector` — subscribes ALL events ที่อ้าง `DeliveryOrderId` | One projector, many event types |
| P2.B3 | Push via `OrderHub.ActivityUpdated` | Group: `order:{id}` |
| P2.B4 | Replace `GET /orders/{id}/audit-full` to query activity table | Same response shape (transparent) |
| P2.B5 | Backfill from existing OrderAuditEvent + ExecutionEvent + amendment table | Multi-source merge |
| P2.B6 | Drop old UNION query implementation | Cleanup |

### 4.2 Frontend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P2.F1 | Migrate `FullAuditLog` to new endpoint (minimal change) | Transparent swap |
| P2.F2 | **Category filter chips** — All/Status/Trip/OMS/POD/Amendment | enhance existing |
| P2.F3 | **Severity color coding** — info/warning/error | visual upgrade |
| P2.F4 | **Group by day** toggle | UX improvement |
| P2.F5 | **Expand row → JSON details modal** | drill-down |
| P2.F6 | **Quick filter "Errors only"** shortcut | shortcut |
| P2.F7 | **Export CSV** button | utility |
| P2.F8 | Live updates via `ActivityUpdated` hub event | uses P0.F3 hook |

### 4.3 Acceptance
- ✅ Page load < 50ms (จาก 200ms+)
- ✅ Category filter instant (no extra query)
- ✅ Backward compat — same response contract
- ✅ Errors visually distinct

### 4.4 P2 Total Effort: **4-5 วัน**

---

## 5. Phase P3 — Dashboard Read Models

**Duration:** 7-10 วัน
**Goal:** Pre-computed KPIs + funnels + utilization — instant dashboards with realtime SignalR push

### 5.1 Backend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P3.B1 | `dashboard.order_status_counts` (hourly bucket) | INCR/DECR on transitions |
| P3.B2 | `dashboard.fleet_utilization_snapshots` (minute snapshots) | Periodic timer + state |
| P3.B3 | `dashboard.dispatch_funnel_hourly` | Multi-column counters |
| P3.B4 | `OrderStatusCountProjector` | Idempotent INCR/DECR via SQL upsert |
| P3.B5 | `FleetUtilizationProjector` (scheduled + event-driven hybrid) | Snapshot every 60s + on state change |
| P3.B6 | `DispatchFunnelProjector` | Aggregate at hour resolution |
| P3.B7 | Push to `DashboardHub.CountersUpdated` via `DashboardCounterBatcher` (250ms batch from P0.B11) | Batched! |
| P3.B8 | Read endpoints — counts, utilization, funnel | Paginated time range |
| P3.B9 | Backfill from historical data | Time-bucketed insert |
| P3.B10 | Replay supports time-range partial rebuild | Operational tool |

### 5.2 Frontend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P3.F1 | API client + proxy routes | dashboard endpoints |
| P3.F2 | Migrate `/dashboard` (overview) | use new endpoints |
| P3.F3 | Migrate `/dashboard/orders` (order analysis) | use new endpoints |
| P3.F4 | Migrate `/dashboard/robots` (robot analysis) | use new endpoints |
| P3.F5 | `<PeriodSelector />` — "24h / 7d / 30d / custom" (fast queries enable this) | reusable |
| P3.F6 | `<DispatchFunnelChart />` — Sankey-style flow | Recharts |
| P3.F7 | `<OrderStatusChart />` — stacked area over time | Recharts |
| P3.F8 | `<UtilizationHeatmap />` — busy hours × day-of-week | custom SVG |
| P3.F9 | Click chart drill-down → order list with prefilter | navigation |
| P3.F10 | DashboardHub subscription + batched updates | uses P0.F3 hook |
| P3.F11 | Lag banner if backplane disconnects | uses P0.F7 |

### 5.3 Acceptance
- ✅ Page load < 200ms (จาก 1-3s)
- ✅ Auto-refresh smooth (no flicker), 250ms batch window
- ✅ Drill-down opens list with applied filters
- ✅ Period change instant (no waterfall query)

### 5.4 P3 Total Effort: **7-10 วัน**

---

## 6. Phase P4 — Search/List Projection

**Duration:** 5-7 วัน
**Goal:** Order list page faster, full-text search, faceted filters

### 6.1 Backend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P4.B1 | `search.order_list_view` denormalized table | ✅ Done — tsvector for FTS, partial indices |
| P4.B2 | `OrderListViewProjector` — subscribes Order + Trip + Job events that affect list view | ✅ Done — UPSERT pattern, 23 IConsumer subscriptions |
| P4.B3 | Search API enhancement — `q` (FTS), `hasFailedTrip`, `hasActiveJob` filters | ✅ Done |
| P4.B4 | Migrate `GET /orders` to query `order_list_view` | ✅ Done — Same response contract |
| P4.B5 | Push to `OrderHub.ListItemUpdated` for live updates | ✅ Done (2026-06-16) — `SubscribeList`/`UnsubscribeList` on `OrderHub`, `IOrderClient.ListItemUpdated`, `PublishOrderListChangedAsync` impl in `SignalROrderRealtimePublisher`, `OrderListViewProjector.Run` pushes hint `(orderId, changeHint)` after every successful store+MarkProcessed. AFTER MarkProcessed so redelivery + dedup skip doesn't fire a duplicate push. |
| P4.B6 | Backfill from existing orders + computed derived columns | ✅ Done |

### 6.2 Frontend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P4.F1 | **Full-text search box** (debounced 300ms) | ✅ Done — `FilterBar` |
| P4.F2 | **Faceted filters** — Has failed trip / Has active job toggles | ✅ Done — inline |
| P4.F3 | **Sort by more columns** — all indexed | ✅ Done |
| P4.F4 | **Infinite scroll mode** option (toggle pagination/scroll) | ✅ Done — `OrdersTable` |
| P4.F5 | **URL state persistence** — filters in query params | ✅ Done |
| P4.F6 | **Saved filters** — localStorage + dropdown | ✅ Done — `saved-filters.tsx` `SavedFiltersMenu`: Bookmark chip in `FilterBar`, dropdown with list/apply/delete + "Save current filters" prompt. Stored as `SavedFilter[]` under localStorage key `orders:saved-filters`. Snapshot = whole filter state object captured at save time. |
| P4.F7 | Live list updates via OrderHub | ✅ Done (2026-06-16) — `useOrderListSubscription` in `lib/realtime/hubs/order-hub.ts`, `orders-experience.tsx` subscribes + 500 ms debounce-refetches `fetchOrders({silent:true}) + fetchStats()`. Hint payload `{orderId, toStatus}` is opaque — refetch keeps server FTS/facets/sort authoritative. |

### 6.3 Acceptance
- ✅ Search typing → results < 100ms
- ✅ Multi-filter combos no slow query
- ✅ Infinite scroll smooth at 10k+ orders
- ✅ Bookmarked URL restores filter state

### 6.4 P4 Total Effort: **5-7 วัน**

---

## 7. Phase P5 — Reporting / BI Projection

**Duration:** 1-1.5 weeks
**Goal:** Self-service analytics — wide fact tables, in-app reports, no write-side contention

### 7.1 Backend Deliverables

| # | Deliverable | Notes |
|---|---|---|
| P5.B1 | `bi.order_facts` wide table | ✅ Done — `bi.OrderFacts` (36 rows) — pre-computed durations, dimensions |
| P5.B2 | `bi.trip_facts`, `bi.job_facts` | ✅ Done — `bi.TripFacts` (29 rows), `bi.JobFacts` (34 rows) |
| P5.B3 | `OrderFactsProjector`, `TripFactsProjector`, `JobFactsProjector` | ✅ Done — UPDATE specific columns per event, IConsumer<T> auto-wired |
| P5.B4 | Read endpoints + report templates | ✅ Done — `ReportsEndpoints` (orders-summary, sla-breach, top-failures, lead-time, orders-export) + `DispatchReportsEndpoints` (vehicle-performance, trips-export) + `PlanningReportsEndpoints` (job-failures, **jobs-export added 2026-06-16**) |
| P5.B5 | Schema permissions — `bi` readable to BI tools, write blocked | ⏭️ Deferred — dev DB has only `postgres` role; rev when staging/prod role separation is set up |
| P5.B6 | Optional: read replica connection string for BI | ⏭️ Deferred — operational (infra ticket, not code) |

### 7.2 Frontend Deliverables (Option B — in-app Reports section)

| # | Deliverable | Notes |
|---|---|---|
| P5.F1 | `/reports` page — template list + recent reports | ✅ Done — `app/reports/page.tsx` + `reports-experience.tsx` (tab nav + window toggle) |
| P5.F2 | Pre-built templates — "Daily Ops Summary", "SLA Performance", "Failure Analysis", "Utilization" | ✅ Done — 6 templates: orders-summary, sla-breach, top-failures, lead-time, job-failures, vehicle-performance |
| P5.F3 | `<ReportBuilder />` — time range + dimensions + metrics | ⏭️ Deferred — 6 fixed templates cover known use cases; revisit when an analyst hits a wall |
| P5.F4 | Export CSV / Excel / PDF | ⚠️ Partial — CSV done for orders + trips + **jobs (added 2026-06-16)**; Excel + PDF deferred (analysts can pivot CSV in Excel — adding xlsx/jsPDF bundles costs ~80kB for marginal gain) |
| P5.F5 | Scheduled reports (backend + UI) | ⏭️ Deferred — needs concrete request (cadence, recipients) before designing |
| P5.F6 | Embed mode — iframe-friendly URLs | ⏭️ Deferred — no external dashboard consumer yet |
| P5.F7 | Left-rail "Reports" section | ✅ Done — `left-rail.tsx` has Reports link with `FileBarChart2` icon |

### 7.3 Acceptance
- ✅ Report on 1M rows < 2s
- ✅ All export formats work
- ✅ Templates cover 80% common use cases
- ✅ BI tool can connect to `bi` schema

### 7.4 P5 Total Effort: **1-1.5 weeks**

---

## 8. Phase P6 — Compliance Hardening (Optional) — ⛔ SKIPPED 2026-06-16

> **Decision (2026-06-16):** Skipped — no concrete regulatory ask. The
> roadmap below stays as a reference for when (if) a compliance
> requirement actually lands. Triggers that would reopen this phase:
> EU/UK customer onboarding (GDPR right-to-access), Thai PDPA audit,
> SOX-style internal financial audit, ISO 27001 / IEC 62443
> certification target, or a customer contract clause asking for
> tamper-evident audit logs. Until then, the existing event log
> (`EventId` + `OccurredOn` + projection lineage) is enough for
> operational debugging and informal forensics — but it has no
> cryptographic tamper-evidence and no enforced retention policy, so do
> not represent it as audit-grade.

**Duration (if reopened):** 1-2 weeks
**Goal:** Tamper-evidence, immutability, audit-grade — only if regulatory need arises

### 8.1 Backend
- P6.B1 — Event archival to cold storage (S3/MinIO) after 30 days
- P6.B2 — Merkle chain on history rows for tamper-evidence
- P6.B3 — Event schema registry + upcasting framework
- P6.B4 — Compliance reports (GDPR data export, SOX audit trail)

### 8.2 Frontend
- P6.F1 — `/admin/compliance` dashboard
- P6.F2 — Audit verification UI (hash chain check)
- P6.F3 — Signed PDF compliance reports download
- P6.F4 — Tamper-evidence badge in timeline

### 8.3 Acceptance
- ✅ External auditor demo passes integrity verification
- ✅ GDPR export request fulfilled < 24h SLA

### 8.4 P6 Total Effort: **1-2 weeks**

---

## 9. Cross-cutting Workstreams

### CC1 — Documentation
**ตลอด project, ฝัง with each phase:**

| Doc | When |
|---|---|
| `docs/projection-conventions.md` | P0 |
| `docs/projector-catalog.md` — every projector with purpose/inputs/outputs | P1+ (live update) |
| `docs/signalr-hub-catalog.md` — every hub + methods + groups | P0 |
| `docs/replay-runbook.md` — replay scenarios + commands | P0 |
| `docs/projection-failure-runbook.md` — DLQ debug, common errors | P0 |
| `docs/scaling-guide.md` — stage 1→4 path | P0 |
| `docs/event-versioning-guide.md` | P0 (lightweight) → P6 (full) |

### CC2 — Testing Patterns
**ตลอด:**
- Unit test: Projector in→out mapping
- Idempotency test: duplicate event → 1 row
- Out-of-order test: event reorder → graceful handling
- Integration test: Postgres + RabbitMQ + SignalR testcontainer
- Replay test: delete read model → replay → match snapshot
- Hub test: subscribe + receive + reconnect

### CC3 — Observability
**ตลอด:**
- Lag metric per projector
- Throughput metric per projector
- DLQ depth alert
- Hub connection count gauge
- Hub method latency histogram
- Replay status dashboard
- OpenTelemetry trace spans

### CC4 — Frontend Patterns
**ตลอด (most concentrated in P0):**
- Eventual consistency UX (freshness chips, lag banners)
- Realtime via SignalR (hooks + components)
- Optimistic updates with reconciliation
- Empty/loading/error states (4-state per projection widget)
- Accessibility (ARIA live regions for realtime)
- Dark mode parity

---

## 10. Timeline + Sequencing

### Sequential (single dev — 8-10 weeks total)

```
Week 1     P0  Foundation (BE+FE)
Week 2     P1  Status History start
Week 3     P1  Status History finish
Week 4     P3  Dashboard start  ← Path A: value-driven (recommended)
Week 5     P3  Dashboard finish
Week 6     P4  Search/List
Week 7     P2  Activity Timeline
Week 8-9   P5  Reporting/BI
Week 10+   P6  Compliance (optional)
```

### Parallel (2 devs — 5-6 weeks total)

```
Week 1
  Dev A: P0 backend (SignalR, idempotency, replay)
  Dev B: P0 frontend (hooks, components, admin page)

Week 2-3
  Dev A: P1 backend (history projectors, queries)
  Dev B: P1 frontend (timeline sections in 3 drawers)

Week 4
  Dev A: P3 backend (dashboard projectors)
  Dev B: P2 backend+frontend (activity timeline)

Week 5
  Dev A: P3 frontend (charts, dashboard migration)
  Dev B: P4 backend+frontend (search/list)

Week 6
  Dev A: P5 backend (BI tables)
  Dev B: P5 frontend (reports module)
```

### Recommended sequencing rationale
**Path A (value-driven):** P0 → P1 → P3 → P4 → P2 → P5 → P6
- P1 = visible UX win, addresses production debug pain
- P3/P4 = user-perceived speed (most impressive demo)
- P2 = polish
- P5/P6 = long-term ops/compliance

---

## 11. Risk Register

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R1 | Projector falls behind (lag) | High | Lag alert (P0.B5), backpressure config, scale-out plan |
| R2 | Read model drift จาก write | High | Replay tooling (P0.B6), nightly reconciliation job (post P1) |
| R3 | Out-of-order events corrupt state | Med | Per-aggregate routing key (P0.B7), OccurredOn guard in projectors |
| R4 | Event schema change breaks projector | Med | V1/V2 coexistence (P0.B3), versioning doc |
| R5 | SignalR connection storm on restart | Med | Exponential backoff + jitter, graceful shutdown |
| R6 | Redis backplane bottleneck | Med | Tune at 5k connections, plan migration to Azure SignalR Service |
| R7 | Backfill blocks production writes | High | Batch SQL, off-hours, monitorable progress |
| R8 | Storage growth (read models) | Low | Partitioning plan, archival policy (P6) |
| R9 | DLQ buildup unnoticed | Med | Alert on DLQ depth (P0), `/admin/projections` UI |
| R10 | Operational complexity ↑ | Med | Runbooks (CC1), team training session |
| R11 | Frontend bundle size ↑ จาก SignalR | Low | `@microsoft/signalr` = ~30KB gzipped — acceptable |
| R12 | WebSocket blocked by client firewall | Low | SignalR auto-fallback to SSE/long-poll built-in |

---

## 12. Decision Log

### D1: Why SignalR over raw WebSocket / SSE / Polling?
- .NET-native, first-class integration
- Auto-fallback (WS → SSE → long-poll) survives corporate firewalls
- Strongly-typed RPC abstraction
- Built-in reconnect with state recovery
- Industry standard for ASP.NET enterprise
- Free + self-hosted suits DTMS scale

### D2: Why MessagePack + LZ4?
- 30-60% smaller payload vs JSON
- 3-5× faster parsing
- Fleet position batches benefit most
- Minimal client/server config

### D3: Why Redis backplane (deferred-activated)?
- DTMS already runs Redis container (zero new infra)
- Single config switch when multi-instance
- Stage 1 (single instance) ใช้ default in-memory
- Stage 2+ flip to Redis with 1-line change

### D4: Why 5 focused hubs over 1 god hub?
- Maps to bounded contexts
- Independent fault isolation
- Page-scoped lazy connection (less idle resource)
- Different throttling per hub
- Per-hub authorization

### D5: Why REST snapshot + SignalR deltas (hybrid)?
- Initial load = REST (cacheable, scalable, CDN-friendly)
- Updates = SignalR push (low latency, low bandwidth)
- Graceful degradation: if hub fails, REST still works
- Standard enterprise pattern

### D6: Why thin hubs (no DB calls)?
- Hub method on hot path — DB call blocks thread
- DB call ใน hub = N+1 problem at scale
- Initial data via REST (proper caching layer)
- Hub = subscription management only

### D7: Why Path A (value-driven) sequencing?
- P1 ตอบ production debug pain ทันที
- P3/P4 = visible "wow" value แสดงผู้บริหารได้
- P2 polish เก็บไว้หลัง
- P5/P6 long-term — เริ่มเมื่อ value validated

### D8: Why Event Projection over EF Interceptor?
- DTMS infrastructure (outbox + events) มีอยู่ครบ
- Reuses pattern team รู้แล้ว (Phase b9 consumers)
- Cleaner separation: domain ≠ projection
- Independent scaling (projector ≠ write side)
- Replay-able (interceptor can't replay)

### D9: Why NOT Event Sourcing?
- DTMS write side = traditional tables (works fine)
- Event Sourcing = massive rewrite
- DDD aggregates ที่มี state mutation work as-is
- Projection achieves 90% of ES benefits at 10% cost

### D10: Why deferred Azure SignalR Service?
- Self-hosted handles up to ~5k concurrent comfortably
- DTMS scale (< 200 users) ห่างไกล
- Migration path clear when needed (1-line config)
- Cost not justified at current scale

---

## 13. Performance Targets

| Metric | Target | Validated in |
|---|---|---|
| Status change → UI render (end-to-end) | < 1s p99 | P1 |
| Order list load (100k orders) | < 50ms p95 | P4 |
| Dashboard page load | < 200ms p95 | P3 |
| Order detail timeline query | < 5ms p99 | P1 |
| Status history query | < 5ms p99 | P1 |
| SignalR hub method latency | < 10ms p99 | P0 |
| Replay throughput | > 1000 events/sec | P0 |
| Concurrent SignalR connections (per instance) | 5000 | Stage 2 |
| Memory per connection | < 50KB | Stage 1 baseline |
| Reconnect time | < 2s p95 | P0 |
| Initial page hub connect | < 100ms p95 | P0 |
| Idle bandwidth per user | < 1KB/min | Steady state |
| Active bandwidth per user | < 50KB/min | High activity |

---

## 14. Implementation Checklist — P0 Start (Week 1)

### Day 1 — Backend foundation
- [ ] Create `SharedKernel/Projection/IdempotentProjector.cs`
- [ ] Create `SharedKernel/Projection/IProcessedEventTracker.cs` + Postgres impl
- [ ] Create `SharedKernel/Projection/Observability/ProjectionMetrics.cs`
- [ ] Add `ICurrentActorContext` middleware
- [ ] Build verify

### Day 2 — Event enrichment + replay
- [ ] Add V2 status events with `TriggeredBy`/`CorrelationId`
- [ ] Update DomainEventMappers to emit V2 (keep V1 for backward compat)
- [ ] Build `IProjectionReplayService` + CLI command

### Day 3 — SignalR core
- [ ] Add NuGet: `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis`
- [ ] Configure SignalR in `Program.cs` with MessagePack + JWT
- [ ] Create 5 hub classes (`OrderHub`, `JobHub`, `TripHub`, `DashboardHub`, `FleetHub`)
- [ ] Add hub endpoint registration
- [ ] Add Redis backplane config (env-var gated)

### Day 4 — Hub filters + throttling
- [ ] Implement `TracingHubFilter`
- [ ] Implement `RateLimitedHubFilter` (token bucket)
- [ ] Implement `DashboardCounterBatcher` (250ms window)
- [ ] Implement `FleetPositionThrottler` (1s + batch)
- [ ] Add health check extension

### Day 5 — Frontend foundation
- [ ] Install `@microsoft/signalr` + MessagePack protocol
- [ ] Build `lib/realtime/signalr-client.ts` singleton manager
- [ ] Build `lib/hooks/use-hub-subscription.ts`
- [ ] Build typed hub wrappers (5 files)
- [ ] Build `<TimelineView />` shared component
- [ ] Build `<DataFreshnessChip />`, `<ConnectionIndicator />`, `<ProjectionLagBanner />`

### Day 6 — Admin page + UX
- [ ] Build `/admin/projections` page
- [ ] Build `<ReplayDialog />`
- [ ] Add left-rail "Admin" link
- [ ] Smoke test reconnect scenarios

### Day 7 — Docs + verify
- [ ] Write `docs/projection-conventions.md`
- [ ] Write `docs/signalr-hub-catalog.md`
- [ ] Write `docs/replay-runbook.md`
- [ ] End-to-end smoke: emit event → projector → hub → browser receives
- [ ] Run all tests + verify lag metric in `/admin/projections`

---

## 15. Stage Gates (proceed only when met)

| Gate | Before proceeding to | Check |
|---|---|---|
| **Gate 1** | P1 | P0 acceptance criteria all green; admin page working; smoke test E2E succeeds |
| **Gate 2** | P3 (next major phase) | P1 production stable 1 week; replay verified on real data |
| **Gate 3** | P5 | Stage 2 (multi-instance) decision made if needed; backplane validated |
| **Gate 4** | P6 | Compliance requirement formally identified (don't speculate) |

---

## 16. What's NOT in scope (explicit non-goals)

- **Event sourcing** — write side stays traditional (write tables remain source of truth)
- **GraphQL API** — REST + SignalR sufficient
- **WebRTC** — no peer-to-peer needs
- **Mobile app** — focus on web; mobile can leverage same hubs later
- **Multi-tenancy** — DTMS single-tenant currently; add later if needed
- **CRDTs** — no collaborative editing requirement
- **Edge deployment** — central hosting fine for current scale

---

## 17. Success Definition

**At end of P0-P3 (≈4-5 weeks):**
- Operators report Dashboard pages "feel instant"
- Status timeline visible in all 3 entity drawers
- Replay tooling used at least once to fix a real read-model bug
- Zero polling-based code paths in user-facing pages
- Production incident MTTR ↓ measurably (timeline = direct debug aid)
- `/admin/projections` page = operator's daily health check

**At end of P0-P5 (≈8-10 weeks):**
- BI team self-serves on `bi.*` schema without write-side impact
- Dashboard load times ≤ p95 200ms sustained
- Order list queries ≤ p95 50ms at 100k+ orders
- SignalR connection stable at expected concurrent user count
- Documentation complete for team onboarding

---

## 18. Cross-references

- Planning workflow roadmap → [planning-workflow-roadmap.md](planning-workflow-roadmap.md)
- Remaining phases plan → [remaining-phases-plan.md](remaining-phases-plan.md)
- Upstream OMS notification plan → [upstream-oms-notification-plan.md](upstream-oms-notification-plan.md)

อัพเดตไฟล์นี้ทุกครั้งที่ phase ใดเข้า main — ให้เป็น single source of truth สำหรับงาน Event Projection
