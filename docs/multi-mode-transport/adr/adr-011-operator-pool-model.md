# ADR-011: Operator Pool Model for Manual/Fleet Dispatch

- **Status**: Accepted
- **Date**: 2026-07-03
- **Deciders**: Solo dev + product decision
- **Related**: [ADR-006](adr-006-transport-mode-feature-flag.md), [Phase 4](../phases/phase-4-transport-manual.md), [Manual Operator API](../api/manual-operator-api.md), [SignalR Hub Catalog](../../signalr-hub-catalog.md)

## Context

Phase 4 shipped Manual mode with **auto-assign**: at dispatch time, the strategy picked an operator via `WarehouseAwareOperatorAssignmentPolicy`, wrote a `ManualTripExtension` row binding trip → operator, and pushed a notification.

That model turned out to be wrong for the actual Delta ops:

1. **Single-active operator constraint** — `Operator.CurrentTripId` locked one operator to one trip. Real ops has many operators, many trips at once, all fungible.
2. **Warehouse-scoped eligibility** — the policy only considered operators in the same warehouse as the pickup. Reality: operators move freely; there's no warehouse-locked crew.
3. **Auto-assignment race** — under load, "no active + idle operator" became a common failure ([observed in the 2026-07-02 OMS pilot](../../plans)). Every operator was busy, so *every* new order failed to dispatch even though ops was ready to take it.
4. **OMS timing** — OMS wanted the shipment notification (`deliveryBy`) at *dispatch* time, not at operator acknowledge. Auto-assign forced the notify to wait for an operator, which sometimes never arrived.

Product decision (2026-07-02):
> "ยกเลือก auto assign แล้วส่ง trip start ให้ OMS ตอน dispatch แล้วเหมือนกันกับ transport แบบ AMR"

Translation: kill auto-assign, notify OMS at dispatch time (like AMR does), let operators pull work from a shared pool.

## Decision

Move Manual/Fleet to a **shared operator pool** with:

- **No pre-assignment at dispatch.** A Trip lands in the pool with `ClaimedByOperatorId = NULL`.
- **Universal visibility.** Every active operator sees every pooled trip. No warehouse or zone filter.
- **Atomic claim + start.** Operator taps "Acknowledge & Start" → a raw SQL CAS binds the trip to them and moves it to `InProgress` in one atomic operation.
- **FIFO fairness.** Pool sorts by `DispatchedAt` ASC; oldest trips get picked first.
- **OMS notify at dispatch.** Fires `TripDispatchedIntegrationEventV1` at dispatch (with `deliveryBy = null`); the claim-time `TripStarted` notify is suppressed for pool trips to prevent duplicate `shipmentId` POSTs.
- **Realtime updates via SignalR.** A dedicated `OperatorPoolHub` broadcasts `PoolTripAdded` / `PoolTripClaimed` / `PoolTripRemoved` to all connected PWAs so lists stay in lock-step with the DB.

### Trip lifecycle (post-pool)

```
Created ──dispatched──► Created + DispatchedAt≠null
        (still Created)      ▲
                             │
       (pool predicate:  Status='Created'
                       ∧ DispatchedAt IS NOT NULL
                       ∧ ClaimedByOperatorId IS NULL)
                             │
                    operator wins CAS
                             ▼
                    InProgress + ClaimedByOperatorId + StartedAt
                             │
                             ▼
                   Pickup ─► Drop ─► Completed
```

Note: there is **no** `Dispatched` status. Manual/Fleet stays `Created` until claimed, matching AMR's Created-until-vendor-accepts contract. Pool membership is a *derived predicate*, not a distinct enum value — this decision was made after prototyping a `TripStatus.Dispatched` value in PR-A and finding that the two-phase lifecycle (`Created → Dispatched → InProgress`) added noise to every projector, timeline UI, and status-history row for no informational gain.

### Race handling

Two operators tap Acknowledge on the same pooled trip:

```sql
-- Runs atomically per client connection; PG row lock serializes them.
UPDATE dispatch."Trips"
   SET "ClaimedByOperatorId" = @op,
       "ClaimedAt"           = @now
 WHERE "Id"                  = @tripId
   AND "Status"              = 'Created'
   AND "DispatchedAt"        IS NOT NULL
   AND "ClaimedByOperatorId" IS NULL
```

- Rowcount = 1 → this operator won → transition to InProgress, materialize `ManualTripExtension`, broadcast `PoolTripClaimed`, return HTTP 204.
- Rowcount = 0 → someone else got the row first → return `AcknowledgeTripErrorCodes.AlreadyClaimed` → endpoint maps to HTTP 409 → PWA shows "คนอื่นรับไปแล้ว" + auto-refreshes the pool list.

The partial index `IX_Trips_Pool` covers the WHERE clause so the CAS is O(log n) even with millions of terminal trips in the table.

### OMS de-duplication

Every Trip lifecycle event fans out to `TripStartedOmsNotifyConsumer`. Without care, a pool trip would notify OMS **twice**:

1. At dispatch via `TripDispatchedIntegrationEventV1` (`deliveryBy = null`)
2. At claim via `TripStartedIntegrationEvent` (`deliveryBy = <operator name>`)

The second POST would replay the same `shipmentId` and clobber OMS's record. The consumer skips the second notify by checking `Trip.DispatchedAt`:

```csharp
if (eventType == "TripStarted" && trip?.DispatchedAt is not null)
{
    _logger.LogInformation("[OmsNotify] pool trip already notified at dispatch time; skipping duplicate TripStarted POST.");
    return;
}
```

AMR trips are unaffected — they never enter the pool, so `DispatchedAt` stays null and the TripStarted POST proceeds as before.

### Reasoning summary

| Criterion | Auto-assign (v1) | Pool (v2) |
|---|---|---|
| Dispatch success under load | ❌ fails when all operators are busy | ✅ trip queues, next-free operator claims |
| Operator agency | ❌ system decides for them | ✅ operator picks what to work on |
| Race safety | N/A (single writer) | ✅ SQL CAS, one wins |
| OMS notify timing | Delayed until operator ack | Immediate at dispatch |
| Warehouse coupling | Locked to policy scope | Universal |
| SLA watchdog | AckSLA / PickupSLA / DropSLA | PickupSLA / DropSLA (Ack collapses into claim) |
| Realtime UX | Push notification per operator | SignalR broadcast to all pool viewers |
| Multi-active operators | ❌ 1 trip max per operator | ✅ unbounded |

## Consequences

**Positive**:
- Zero-touch dispatch success rate: any pooled trip is claimable as soon as any operator is idle.
- Cross-warehouse balancing emerges naturally (whoever has time claims).
- OMS `shipmentId` semantics simplified — one POST per shipment lifecycle.
- Operator PWAs stay in sync via SignalR without polling.

**Negative**:
- No system-enforced fairness *between operators*: a fast tapper wins over a slow one. Acceptable for the current team of ~10 operators; may need queueing hints (e.g. "next up: Alice") if the pool grows to 50+.
- Dispatcher visibility is now derived (query the pool) rather than pushed (a list of assignments). Addressed in PR-G (dispatcher board).
- Legacy admin reassign (`ReassignManualTripCommand`) is inconsistent with the pool model — force-assigning a claimed trip elsewhere breaks the single-owner invariant. Slated for removal in a follow-up cleanup PR.

**Neutral (deferred)**:
- `Operator.CurrentTripId` column becomes dead data (multi-active is now the rule). Column stays for now because deleting it touches Trip repo, GetMyProfile, ListOperators, ReassignManualTrip, and the sync service simultaneously. Follow-up drop after 1-2 sprints of soak.
- `ManualTripExtension.AckDeadline` column is meaningless in the pool model (ack + claim happen together) but still written by admin reassign. Follow-up drop paired with reassign removal.

## Alternatives considered

### A. Keep auto-assign but expand eligibility
Drop the warehouse-scoped filter but keep the auto-picker. Rejected — still fails under load (auto-picker can't pick anyone when everyone's busy) and doesn't give operators agency.

### B. Zone-partitioned pool
Split the pool by `parentLocationCode` (WMS zone). Rejected per user decision on 2026-06-30: "ทุกคนทำได้ทุกที่" (everyone can work anywhere). Universal visibility beat zone isolation.

### C. Distinct `TripStatus.Dispatched` state
Prototyped in PR-A but rolled back in PR-C. Adding a state added noise to every projector and history table for no informational gain — the derived predicate (`Status=Created ∧ DispatchedAt≠null ∧ ClaimedByOperatorId IS NULL`) is enough, and it keeps the Manual lifecycle isomorphic to AMR's.

### D. Distributed lock (Redis) instead of SQL CAS
Would work but adds a moving part (lock TTL, lock cleanup on crash). SQL CAS is atomic at the row level in Postgres and needs no auxiliary service. Redis is already used for SignalR backplane; we didn't want to broaden its role to include coordination-critical locks.

### E. Push notification instead of SignalR
Every operator has a `PushNotificationSubscription` from the earlier auto-assign flow. Pushing on every pool change would work but takes ~seconds per delivery and can't guarantee ordering (multiple ADDs vs a CLAIMED could race). SignalR keeps latency in the 10-30 ms range and preserves event order per connection. Push is deferred to PR-I as a *supplementary* channel for offline operators.

## Implementation trail

| PR | Files | What it did |
|---|---|---|
| PR-A | Migration + `TripStatus` enum + Trip aggregate fields (`DispatchedAt`, `ClaimedByOperatorId`, `ClaimedAt`) + partial indexes `IX_Trips_Pool` and `IX_Trips_ClaimedByOperatorId_Active` | Schema foundation |
| PR-B | `Trip.MarkDispatched`, `TripDispatchedDomainEvent`, `TripDispatchedIntegrationEventV1`, `ManualDispatchStrategy` pool branch (behind `PoolMode` feature flag), OMS consumer subscribes to both TripStarted and TripDispatched | Dispatch flow |
| PR-C | Drop `TripStatus.Dispatched`, `ITripRepository.TryClaimFromPoolAsync` (raw SQL CAS), `AcknowledgeTripCommandHandler` pool branch, endpoint `HTTP 409` mapping, OMS consumer duplicate-skip guard | Claim flow |
| PR-D | `OperatorPoolHub`, `IOperatorPoolBroadcaster`, `GetPoolTripsQuery`, REST `GET /api/operator/trips/pool`, frontend `/m/pool` page with reducer + preview drawer | Frontend + realtime |
| PR-E | Deleted `IOperatorAssignmentPolicy`, `WarehouseAwareOperatorAssignmentPolicy`, legacy branch of `ManualDispatchStrategy`, `PoolMode` feature flag, 6 legacy test files | Cleanup |
| PR-F | `AcknowledgeTripHandlerTests` (9 tests), `TripPoolTransitionTests` (4 tests) | Regression protection |

## Verification evidence

E2E in the 2026-07-03 pilot session:

- **Happy path** — 31ms round-trip from operator tap to Trip.InProgress + SignalR broadcast + PWA navigate. (`[AckPool] ✓ Operator … claimed trip …`)
- **Race** — Two operators taped acknowledge on the same trip within the same millisecond. `titpooja` lost (`[AckPool] claim lost by operator … — another operator got there first`), returned 409 in 9ms. `Tuinui aiai` won, returned 204 in 21ms. No double-write, no double OMS POST.
- **Duplicate OMS notify prevention** — `[OmsNotify] Trip … — pool trip already notified at dispatch time (DispatchedAt=…); skipping duplicate TripStarted POST.`
- **LDAP auth** — 2 distinct real Delta operators tested (bypass disabled per `project_auth_bypass_disabled` decision).
