# DTMS — Event Projection Plan

**Living document.** Update this file when a phase ships, deferred items
get scheduled, or the scope changes. Source of truth for *what we're
building and why*; companion to `docs/projection-conventions.md` which
captures the *how*.

---

## Status Dashboard

| Phase | Scope | Status | Notes |
|---|---|---|---|
| **P0** | Foundation (idempotency, metrics, replay contract, FE primitives) | ✅ **Done** | Pragmatic subset shipped; hardening items deferred |
| **P1** | Status History (b12) — Order/Job/Trip transitions → 3 read models + UI timelines | ✅ **Done** | End-to-end across all 3 aggregates + 3 drawer integrations |
| **P2** | Activity Timeline — unified per-order event feed | ✅ **Done** | Transparent swap of `/audit-full` endpoint; 4-source UNION replaced with single indexed read |
| **P3.1** | Order funnel hourly projection + dashboard KpiRail/DispatchFunnel real data | ✅ **Done** | Recharts installed, useProjectionPoll hook, /api/dashboard/order-funnel endpoint live |
| **P3.2** | Fleet projections (VehicleStateHistory + utilization) + /dashboard/orders + /dashboard/robots subpages | 🔜 **Next** | Completes the original P3 scope |
| **P4** | Search/List projection — denormalized order list view | ⏳ Planned | Adds full-text search |
| **P5** | Reporting/BI projection — wide fact tables | ⏳ Planned | Enables analyst self-service |
| **P6** | Compliance — immutability, tamper-evidence | ⏳ Optional | Only if regulated audit becomes a requirement |

**Overall progress:** ~58% (3 phases done + P3.1 substep, P3.2 next)

---

## Decision Log

Entries appended on every architectural choice that locks future work.

| Date | Decision | Why |
|---|---|---|
| 2026-06-12 | Pattern: Event Projection (not Event Sourcing) | DTMS already has Outbox + integration events + DDD aggregates; projection layers on without rewriting write model |
| 2026-06-12 | Strategy A (Inbox table) preferred over UNIQUE-constraint dedup | Atomicity in same SaveChanges as read-model row; works uniformly across all modules |
| 2026-06-12 | Per-module inbox table (not global) | Each module owns its own DbContext + transactional boundary |
| 2026-06-12 | Replay contract ships in P0, real impl deferred | Avoid premature investment; first projector bug triggers the build |
| 2026-06-12 | Event V1 → V2 enrichment (TriggeredBy/CorrelationId) deferred from P0 | V1 already carries `Reason`; adding actor needs ambient context wiring — not blocking P1 |
| 2026-06-12 | Tests live in module UnitTests projects | Avoid creating a dedicated SharedKernel test csproj until proliferation justifies it |
| 2026-06-12 | Per-aggregate RabbitMQ routing deferred from P0 | MassTransit default ordering OK for status-history; revisit if out-of-order observed |
| 2026-06-12 | FromStatus = NULL on first row per aggregate | Simpler; no read-modify-write race when chaining FromStatus from prior ToStatus |
| 2026-06-12 | Out-of-order events skipped + warning-logged | Preserves chain integrity; RabbitMQ default ordering is good enough that this fires rarely |
| 2026-06-12 | Aggregate build order: Order → Job → Trip | Ship value incrementally + de-risk pattern on simplest aggregate first |
| 2026-06-12 | Refactored IdempotentProjector usage from base class to inline pattern | Single base class can't subscribe to >1 event; multi-event projectors implement IConsumer<T> per event + shared private `Project` helper |
| 2026-06-12 | Projection store + read repo as concrete-typed-per-module abstractions | Avoid global IProjectionInboxRepository DI conflict; each module owns its own concrete |
| 2026-06-12 | Job side: added 6 new V1 integration events + updated mapper | Existing PlanningDomainEventMapper only mapped JobCommitted → couldn't power projection without full coverage |
| 2026-06-12 | Trip side: added TripPausedV1 + TripResumedV1 + carry-forward order/job ids | Domain payload for pause/resume doesn't include order/job ids; projector reads from latest history row instead of write side |
| 2026-06-12 | Trip read model has nullable DeliveryOrderId/JobId | Pause/Resume events have only TripId; nullable is honest about the boundary |
| 2026-06-12 | StatusTimelineSection self-hides on empty + soft-fails on error | Legacy pre-backfill entities shouldn't show empty UI; fetch errors should be visible so ops investigates |
| 2026-06-13 | P2 scope = MVP coverage (project available integration events + backfill historical) | User chose MVP path over "Full" which would require adding 5+ new integration events. Coverage gap for POD/OMS/TripRetry/admin going forward is documented; can be closed in P2.5. |
| 2026-06-13 | Transparent swap of `/audit-full` endpoint (vs. new endpoint) | Reuses `<FullAuditLog />` UI untouched. Category → legacy `Source` string mapping in handler keeps frontend filter chips + colour palette working. |
| 2026-06-13 | Pause/Resume/ExceptionRaised events skipped — no DeliveryOrderId | Carrying forward an OrderId from prior history rows (like Trip side does) doesn't work here because Activity projector doesn't read its own history (only inbox). Cleanest fix is enriching the events later. |
| 2026-06-13 | OrderActivity uses category discriminator + denormalized columns instead of jsonb payload | Earlier plan called for jsonb; pragmatic MVP keeps schema flat — RelatedTripId/AttemptNumber are the only category-specific fields and they fit cleanly as nullable columns. Re-introduce jsonb only when a category needs richer payload. |
| 2026-06-13 | P3.1 splits the original P3 scope into "Order side now, Fleet side + subpages next" | Shipping the Order funnel projection + visible dashboard wiring delivers most of the user-visible win without blocking on Fleet integration events. The split preserves momentum + lets P3.2 reuse the Recharts + useProjectionPoll infrastructure already in place. |
| 2026-06-13 | Combine OrderStatusCounts + DispatchFunnel into a single hour-bucketed wide row | Original plan called for two separate projections; collapsing them avoids double-writes from the same set of integration events and keeps the dashboard query single-table. UI sums or reads columns as needed. |
| 2026-06-13 | useProjectionPoll lives in `lib/hooks/` instead of a fancier global cache (SWR/Tanstack-Query) | Minimal hook fits this codebase's existing pattern of inline fetchers + simple abort-on-rerun. Promote to a global cache only when ≥3 widgets share a fetch. |
| 2026-06-13 | Funnel poll cadence = 15s, freshness banner threshold = 5min (from P0's chip default) | Bucket data ticks at most once per hour, so 15s polling is generous. The 5-min stale threshold gives ops a clear visual cue when the projector or bus stalls. |

---

## P0 — Foundation ✅ Done

**Delivered:**

| Layer | Artifact |
|---|---|
| Doc | [projection-conventions.md](projection-conventions.md) — pattern guide |
| Backend (SharedKernel) | `Projection/InboxMessage.cs`, `IProjectionInboxRepository.cs`, `IdempotentProjector<TEvent>.cs`, `ProjectionMetrics.cs`, `IProjectionReplayService.cs`, `ProjectionServiceCollectionExtensions.cs` |
| Backend (Api/Program.cs) | OTel `.WithMetrics().AddMeter("DTMS.Projection")` + `AddProjectionFoundation()` |
| Tests | `IdempotentProjectorTests.cs` — 4 cases (new / duplicate / permanent fail / transient fail) |
| Frontend | `components/projection/data-freshness-chip.tsx`, `components/projection/timeline-view.tsx` |

**Deferred from P0** (revisit before next phase if pain emerges):

| Item | Trigger to revisit |
|---|---|
| P0.2 — Integration event V2 (TriggeredBy, CorrelationId) | First compliance/audit request that needs actor on every row |
| P0.5 — Per-aggregate RabbitMQ routing key | First observed out-of-order projection bug |
| P0.F2 — `<ProjectionLagBanner />` | When ops complain about stale data not being visible |
| P0.F3 — `useProjectionPoll` hook | First dashboard widget that needs auto-refresh (likely P3) |
| P0.F5 — `/admin/projections` page | After P1 ships and there are projectors worth monitoring (now true — schedulable) |
| P0.F6 — Left-rail "Admin" link | Pairs with P0.F5 |
| P0.F7 — Replay trigger UI | After replay implementation lands |
| Full Replay service implementation | First projector bug that needs a rebuild |

---

## P1 — Status History (b12) ✅ Done

**Verified end-to-end:** Order detail drawer + Trip detail drawer + Job detail drawer each show a structured status timeline backed by an aggregate-specific projection. Real event flow tested: Release Held order → Confirmed → Planning consumer creates Job → JobCreated event → projector → drawer shows "(initial) → Created", then dispatch fails → JobFailed event → projector → "Created → Failed" with vendor reason.

### Backend delivered (per aggregate × 3)

| Aggregate | Tables | Projector | Endpoint |
|---|---|---|---|
| Order | `deliveryorder.OrderStatusHistory` + `deliveryorder.ProjectionInbox` | `OrderStatusHistoryProjector` (subscribes to 11 DeliveryOrder integration events) | `GET /api/v1/delivery-orders/{id}/status-history` |
| Job | `planning.JobStatusHistory` + `planning.ProjectionInbox` | `JobStatusHistoryProjector` (subscribes to 7 Planning integration events) | `GET /api/v1/planning/jobs/{id}/status-history` |
| Trip | `dispatch.TripStatusHistory` + `dispatch.ProjectionInbox` | `TripStatusHistoryProjector` (subscribes to 6 Dispatch integration events) | `GET /api/v1/dispatch/trips/{id}/status-history` |

**Plus:**
- Updated `PlanningDomainEventMapper` to map 6 new Job lifecycle events (only JobCommitted was mapped before).
- Added 2 new Dispatch integration events: `TripPausedIntegrationEventV1`, `TripResumedIntegrationEventV1`.
- Updated `DispatchDomainEventMapper` to map them.
- 3 idempotent backfill SQL scripts under `scripts/backfill-p1-*.sql`.
- DI wiring in `ModuleServiceRegistration.cs` for all three modules.

### Frontend delivered

| Layer | Artifact |
|---|---|
| API client | [lib/api/status-history.ts](../frontend/lib/api/status-history.ts) — 3 fetchers + shared `StatusHistoryEntry` type |
| Proxy routes | 3 Next.js routes mirroring backend paths |
| Shared section component | [components/projection/status-timeline-section.tsx](../frontend/components/projection/status-timeline-section.tsx) — composes `<TimelineView />` + `<DataFreshnessChip />` from P0 |
| Drawer integration | Order detail drawer, Trip detail drawer, Job detail drawer (in JobsExperience) |

### Tests (23 new unit tests across modules)

| File | Cases |
|---|---|
| `IdempotentProjectorTests.cs` (P0) | 4 |
| `OrderStatusHistoryProjectorTests.cs` | 8 |
| `JobStatusHistoryProjectorTests.cs` | 7 |
| `TripStatusHistoryProjectorTests.cs` | 8 |

Each suite covers: first event mapping, derived-FromStatus chaining, duplicate dedup, out-of-order skip, reason-carrying events, permanent failure swallow, transient failure rethrow. Trip suite also covers carry-forward of `DeliveryOrderId`/`JobId` from prior history row.

### Coverage gaps (acknowledged, not blocking)

| Aggregate | Missing transitions | Why | Resolution path |
|---|---|---|---|
| Order | Submitted, Validated, Planning, Planned | No integration events emitted (Planning/Planned are deliberately internal-only per existing code comment) | Add events when an operator use case needs them |
| Job | Assigned | Existing `JobAssignedIntegrationEvent` shape doesn't match projector needs cleanly | Synthetic event when an Assigned-step use case lands |
| Trip | Created | `Trip.CreateForEnvelope` doesn't emit a domain event | Seeded by backfill SQL — covers existing data |

---

## P2 — Activity Timeline ✅ Done

**Verified end-to-end:** the legacy `GET /delivery-orders/{id}/audit-full`
endpoint now reads from a single indexed `deliveryorder.OrderActivity`
projection instead of unioning 4 source tables at query time. API contract
unchanged (FullOrderAuditDto / FullAuditEntryDto) so the existing
`<FullAuditLog />` frontend component works untouched. Live event flow
tested: Hold order → `DeliveryOrderHeldIntegrationEventV1` → projector
appends `OrderHeld` row → endpoint returns updated entries within seconds.

### Backend delivered

| Layer | Artifact |
|---|---|
| Read model | `deliveryorder.OrderActivity` table + `Migrations/20260612181438_AddOrderActivityProjection.cs` |
| Store + read repo | `IOrderActivityProjectionStore` / `IOrderActivityReadRepository` + Infrastructure impls |
| Projector | `OrderActivityProjector` subscribes to 11 Order + 8 Trip integration events |
| Swapped query | `GetFullOrderAuditQueryHandler` now reads from projection; category → legacy `Source` mapping preserves taxonomy |
| Backfill | `scripts/backfill-p2-order-activity.sql` seeds historical rows from the 4 legacy sources (OrderAuditEvents, OrderAmendments, ExecutionEvents, TripRetryEvents) |
| DI | `IOrderActivityReadRepository` + `IOrderActivityProjectionStore` registered alongside the P1 status-history pair |

### Coverage matrix (MVP scope)

| Source | Going forward | Historical |
|---|---|---|
| Order lifecycle events (11) | ✅ Projected from integration events | ✅ Seeded from OrderAuditEvents |
| Order amendments | ✅ Projected | ✅ Seeded from OrderAmendments |
| Trip lifecycle events (Started/Pickup/Drop/Completed/Failed/Cancelled) | ✅ Projected | ✅ Seeded from ExecutionEvents |
| Trip Pause/Resume/ExceptionRaised | ❌ (events don't carry DeliveryOrderId) | ✅ Seeded |
| Item POD scans | ❌ (no integration event yet) | ✅ Seeded from OrderAuditEvents bucket |
| Upstream OMS notify outcomes | ❌ (no integration event yet) | ✅ Seeded from OrderAuditEvents bucket |
| Trip retry triggers | ❌ (no integration event) | ✅ Seeded from TripRetryEvents |
| Admin actions (OrderReopened / OrderAbandoned) | ❌ (audit-only writes) | ✅ Seeded |

**P2.5 hardening (deferred — track when ops needs it):**
- Add integration events for POD scans, OMS notify outcomes, TripRetry triggers, admin actions so the "going forward" gaps close without backfill.
- Add `DeliveryOrderId` to TripPaused/Resumed/ExceptionRaised payloads so the projector can attach them to the order timeline.

### Frontend

No changes — `<FullAuditLog />` component reads the same endpoint contract.
A future P2.5 work item can add category filter chips + `<DataFreshnessChip />`.

### Tests

9 new unit tests in `OrderActivityProjectorTests.cs` covering Order
lifecycle / Amendment / Trip lifecycle mapping + idempotency + skip rules
(empty OrderId, Pause/Resume/ExceptionRaised lacking order context) +
permanent/transient failure handling.

---

## P3.1 — Order Funnel Dashboard ✅ Done

**Verified end-to-end:** `/dashboard` page renders the KpiRail tiles and
DispatchFunnel chart from real `deliveryorder.OrderFunnelHourly` data
instead of the hard-coded mock data they used previously. Polling
auto-refreshes every 15s; `<DataFreshnessChip />` shows the last update.

### Backend delivered

| Layer | Artifact |
|---|---|
| Read model | `deliveryorder.OrderFunnelHourly` — hour-bucketed counter row, one column per status, UNIQUE on BucketHour |
| Projector | `OrderFunnelProjector` subscribes to 10 Order lifecycle integration events, INCRs the matching column for the event-hour bucket |
| Query + endpoint | `GetOrderFunnelQuery` + `GET /api/v1/dashboard/order-funnel?fromUtc=&toUtc=` (defaults to last 24h, capped at 90 days) |
| Backfill | `scripts/backfill-p3-order-funnel-hourly.sql` — aggregates existing OrderStatusHistory into hour buckets via SUM(CASE …) pivot |
| Tests | `OrderFunnelProjectorTests` — 7 cases covering mapping, duplicates, transient/permanent failure, and unit tests for `OrderFunnelHourlyRow.IncrementStatus` |

### Frontend delivered

| Layer | Artifact |
|---|---|
| Library | `recharts` ^3.8 installed (used in P3.2 chart components) |
| Hook | `lib/hooks/use-projection-poll.ts` — initial fetch + interval poll + visibility-aware pause + manual refresh, with AbortController so slow responses can't overwrite newer data |
| API client | `lib/api/dashboard.ts` + Next.js proxy at `/api/dashboard/order-funnel` |
| `<KpiRail />` | Now 4 real KPIs: Confirmed (24h), In flight, Completed (24h), Lost (24h) — pulled from totals |
| `<DispatchFunnel />` | Now 5 real stages mapped from totals: Confirmed → Dispatched → In progress → Completed → Lost (Failed + Cancelled + Rejected). Refresh button + freshness chip |

### Frontend defaults

- Window: trailing 24h, end-exclusive on the current hour
- Poll cadence: 15s; pauses while the tab is hidden, resumes on focus
- `<DataFreshnessChip />` shows on the first KPI tile + the DispatchFunnel
  header

---

## P3.2 — Fleet projections + dashboard subpages 🔜 Next

Completes the original P3 scope:

1. **VehicleStateHistory projection** in `fleet` schema — same shape as
   the P1 status-history projectors but for `VehicleStateChangedIntegrationEvent`.
2. **FleetUtilizationHourly snapshot job** — hourly background service
   that snapshots vehicle states into a `fleet.FleetUtilizationHourly`
   table. Hybrid pattern: event-driven for state history, periodic
   snapshot for utilization.
3. **`/dashboard/orders` subpage** — extended order analysis using the
   existing `OrderFunnelHourly` data + a Recharts stacked-area chart of
   hourly bucket counts over the trailing 7 days.
4. **`/dashboard/robots` subpage** — vehicle utilization chart + state
   distribution snapshot using FleetUtilizationHourly.

---

## P4 — Search/List Projection ⏳ Planned

Denormalized `search.order_list_view` table with Postgres `tsvector` for full-text search. Enables instant list filtering at any scale.

Frontend: full-text search box, faceted filters, infinite scroll mode.

**Effort:** ~M.

---

## P5 — Reporting/BI Projection ⏳ Planned

Wide `bi.order_facts` / `bi.trip_facts` / `bi.job_facts` tables with denormalized timestamps for every status. Enables analyst self-service without touching the write side.

Optional: in-app `/reports` page with template builder + CSV/Excel/PDF export.

**Effort:** ~L.

---

## P6 — Compliance ⏳ Optional

Triggered only by a regulatory requirement:
- Event archival to cold storage
- Tamper-evident row chaining (Merkle hash)
- Event versioning + upcasting framework
- Compliance reports (signed PDF per aggregate)

**Effort:** ~L. Skip unless DTMS enters a regulated context.

---

## Cross-Cutting Workstreams

### CC1 — Documentation
- `docs/projection-conventions.md` ✅ shipped in P0
- `docs/event-projection-plan.md` ✅ this doc (living)
- `docs/projector-catalog.md` — TODO: list of projector classes + read models they own + events they subscribe
- `docs/replay-runbook.md` — created when replay impl lands
- `docs/projection-failure-runbook.md` — created on first DLQ incident

### CC2 — Testing patterns
- Idempotency test ✅ pattern established in P0
- Per-projector unit tests ✅ pattern established in P1 (8/7/8 tests per aggregate)
- Integration tests (real Postgres + RabbitMQ) — add when projector cross-cuts more than one DbContext

### CC3 — Observability
- `dtms.projection.*` metrics ✅ wired in P0; emitted by every projector built in P1
- Grafana dashboard JSON committed under `ops/dashboards/` — TODO after first projector lives long enough to need lag visibility
- Alert rules for lag > 60s, DLQ depth > 0 — TODO

### CC4 — Frontend patterns
- `<DataFreshnessChip />` ✅ shipped in P0
- `<TimelineView />` ✅ shipped in P0
- `<StatusTimelineSection />` ✅ shipped in P1 (composes P0 primitives)
- `<ProjectionLagBanner />` — defer until lag is a real concern
- `useProjectionPoll` hook — defer until P3 (dashboards need it)
- `/admin/projections` page — schedulable now that there are 3 projectors worth monitoring

---

## What to do next (after P1)

**Recommended sequencing:**

1. **Commit P0 + P1 work** to Git as a coherent series:
   - One commit per phase (P0 foundation; P1 Order/Job/Trip — possibly 3 sub-commits)
   - Reference `docs/event-projection-plan.md` from each commit message
2. **Pick P2 (Activity Timeline) as next phase** — completes the "operator timeline" story by unifying everything into one view, retires the `FullAuditLog` UNION query.
3. **Alternatively, ship P3 (Dashboard) for user-visible speed wins** if performance complaints from dashboard usage are coming in.

---

## How to read this doc going forward

- **Plan changes scope?** Update the relevant phase section + Decision Log.
- **Phase ships?** Mark ✅ Done, summarize delivered artifacts, list deferred items.
- **Architectural question comes up?** Check Decision Log first; if not there, decide + add entry.
- **New phase added?** Slot into the Status Dashboard table at the right priority, add a detail section below.
