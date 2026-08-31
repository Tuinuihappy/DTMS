# ADR-019: Carrier Tracking in the Fleet Module

- **Status**: Accepted
- **Date**: 2026-08-28
- **Deciders**: TUINUI
- **Related**: [ADR-001](adr-001-multi-mode-transport-split.md), [ADR-002](adr-002-facility-station-hierarchy.md), [ADR-003](adr-003-trip-extension-tables.md), [ADR-017](adr-017-permission-naming-standard.md)

## Context

DTMS records which **vehicle** ran a trip (`Trip.VehicleId`, `AmrTripExtension.VendorVehicleKey`) and which **items** it carried (`dispatch.TripItems`), but nothing about the physical **carrier** — the rack, cart, or trolley the goods actually sit on. Operations cannot answer "which cart carried order SO-88213?" or "how many trips did CART-0001 run this month, and what was on it?"

Two pieces of the puzzle already existed but were never finished:

- [`CarrierTypeProfile`](../../../src/Modules/Facility/DTMS.Facility.Domain/Entities/CarrierTypeProfile.cs) — a carrier **class** (Code, AMRCapability, MaxWeightKg, MaxSlots) with HTTP CRUD and an admin page. 1 row in the dev database.
- [`LoadUnitProfile`](../../../src/Modules/Facility/DTMS.Facility.Infrastructure/Migrations/20260506082818_AddCarrierTypeAndLoadUnitProfiles.cs) — a packaging spec keyed to a carrier type. Zero rows, zero readers outside its own CRUD.

Neither models an **instance**: there is no `CART-0001` anywhere in the system, so there is nothing to attach to a trip.

The vendor cannot help. [`Riot3NotifyPayload`](../../../src/Modules/Transport.Amr/DTMS.Transport.Amr/Models/Riot3NotifyPayload.cs) carries station and vehicle identity only — RIOT3 never reports which rack a robot lifted. Any carrier data must originate inside DTMS.

Requirements:

1. **Instance-level identity** — track `CART-0001`, not just "a rack of type RACK-A", because the operational question is about a specific physical asset.
2. **One trip, many carriers** — a robot tow train or a two-cart manual move must both be representable.
3. **Item-level manifest** — know which items rode on which carrier, without slot/position granularity.
4. **Multiple capture paths** — transport happens several ways (AMR, Manual operator pool, self-managed source systems), and the carrier can become known at order creation, at dispatch, or at pickup. One hard-coded path would leave whole modes with no data.
5. **Historical truth survives master-data edits** — a report on a retired carrier must still read correctly.
6. **Do not destabilise Dispatch** — `Trip.cs` is the most concurrency-sensitive file in the system (~22 webhook frames per trip, a reconciler, `xmin` optimistic concurrency, and known webhook/command races).

ปัญหาที่ต้องตัดสิน:

1. Which module owns carrier data — Facility (where the type profile lives today) or Fleet?
2. Where does the trip↔carrier binding live, given requirement 6?
3. How does a binding get created when the capture path differs per transport mode?
4. What happens to `LoadUnitProfile`, which is dead code sitting next to the entity we want to move?

## Decision

**Fleet owns the entire carrier domain** — type catalogue, instance registry, trip binding, and load manifest — with binding sourced through a **strategy registry keyed by `TransportMode`**, and with **intent separated from actuality**.

```csharp
// Per-mode binding behaviour, discovered via IEnumerable<> exactly like
// IDispatchStrategy / DispatchStrategyRegistry.
public interface ICarrierBindingStrategy
{
    TransportMode Mode { get; }
    CarrierBindingPolicy Policy { get; }
    Task<Result<CarrierBindingOutcome>> ResolveAtDispatchAsync(
        TripBindingContext ctx, CancellationToken ct);
}

public sealed record CarrierBindingPolicy(
    bool AutoAllocateFromPool,   // no plan supplied → claim one from the pool
    bool RequireConfirmation,    // advisory vs blocking
    bool AllowScanOverride);     // may an operator scan replace a planned carrier
```

### Decision A — Fleet, not Facility

The question splits in two. `CarrierType` (a specification) genuinely could live in either module. `Carrier` (a tracked physical asset) could not — and the instance decides, with the type following so the domain is not split across modules again.

| Property of `Carrier` | Fleet already has this | Facility |
|---|---|---|
| per-unit identity + barcode | `Vehicle` | — |
| status changing over time | `VehicleState` | — (`Map`/`Station` are static) |
| enters/leaves maintenance | `MaintenanceRecord` | — |
| "where is it now" | `Vehicle.CurrentNodeId` | — |
| status history | `VehicleStateHistory` projection | — |
| utilization reporting | `FleetUtilizationHourly` | — |
| assigned to work | `Trip.VehicleId` | — |

Putting `Carrier` in Facility would mean rebuilding an asset-lifecycle subsystem that Fleet already runs. Bounded-context ownership agrees: the people who register a cart and send it for repair are the people who manage robots, while `Map`/`Station` are owned by engineering and synced from RIOT3. After the 2026-08-14 dead-domain purge Facility holds only `Map` and `Station` — static site topology. A carrier moves; it is not topology.

> **Naming caveat, deliberately recorded.** "Fleet" in this system connotes the *robot* fleet: `Vehicle` carries `AdapterKey = "riot3"` and `VendorVehicleKey`. Carriers are **not** known to RIOT3 at all. To keep that unambiguous, `Carrier` must **never** gain `VendorVehicleKey` or `AdapterKey` columns. A future reader seeing `fleet.Carriers` beside `fleet.Vehicles` should not conclude carriers are vendor-synced.

### Decision B — the binding lives in Fleet too, fed by Trip integration events

`fleet.CarrierAssignments` stores `TripId` as a plain `uuid` with no foreign key. Fleet learns trip state by subscribing to the `TripDispatched` / `TripStarted` / `TripCompleted` / `TripFailed` / `TripCancelled` / `TripRejected` integration events Dispatch already publishes, materialising them into a small `fleet.ActiveTrips` projection using the same `ProjectionInbox` de-duplication pattern as [`VehicleStateHistoryProjectionStore`](../../../src/Modules/Fleet/DTMS.Fleet.Infrastructure/Projections/VehicleStateHistoryProjectionStore.cs).

The single consumer does double duty: it keeps the trip-state view current *and* releases carriers when a trip goes terminal.

### Decision C — intent and actuality are different records

- `fleet.CarrierPlans` — *intent*. "This order should use CART-0001" or "…any carrier of type RACK-A." Written by whoever knows first: upstream system, the order-creation UI, an `OrderTemplate` default, or a planner. Because it lives in `fleet.*`, no DeliveryOrder contract change is needed to support it.
- `fleet.CarrierAssignments` — *actuality*, carrying `Source` and `Confidence` (`Planned` | `Confirmed`).

Precedence, highest wins:

```
5  OperatorScan / SourceSystem  → Confirmed   ← always wins
4  Admin (manual fix + reason)  → Confirmed
3  AutoAllocated (from pool)    → Planned
2  OrderRequested (from a plan) → Planned
1  TemplateDefault              → Planned
0  nothing                      → advisory metric + "no carrier" chip
```

A scan that contradicts a plan does **not** delete the plan: the existing row closes with `DetachReason = 'SupersededByScan'` and a new row opens. The append-only table therefore records "planned CART-0001, actually used CART-0007", which is itself a useful signal (`dtms_carrier_plan_vs_actual_mismatch_total`).

This mirrors an idiom the codebase already uses: `RequestedTransportMode` vs the mode actually dispatched, `TemplateNameAtDispatch` vs `VendorFinalSnapshot`, `Item.PickupLocationCode` vs the frozen `Trip.PickupLocationCode`.

### Decision D — delete `LoadUnitProfile`, keep `Item.LoadUnitProfileCode`

The catalogue is removed outright. Two facts justify it: its only readers were `GetLoadUnitProfilesQuery` and `RegisterLoadUnitProfileCommand` (its own CRUD), and the table held zero rows.

`Item.LoadUnitProfileCode` is a different thing and stays. It is a plain `string?` that was **never** validated against the catalogue — [`DraftItemDtoValidator`](../../../src/Modules/DeliveryOrder/DTMS.DeliveryOrder.Application/Commands/CreateDraftDeliveryOrder/ItemDtoValidators.cs) only enforces `MaximumLength(50)` — so removing the catalogue leaves its behaviour bit-for-bit identical. It appears in the shared `ItemDto` used by both the UI and `POST /api/v1/source/delivery-orders`, so removing the column is a breaking public-contract change and is tracked as separate work (deprecate and measure → snapshot V2 → `DROP COLUMN`).

## Alternatives Considered

### Alternative A: Keep everything in Facility

Leave `CarrierTypeProfile` where it is and add `Carrier` next to it.

**Pros:**
- Zero migration risk — no schema move, no permission rename, no frontend churn.
- `AMRCapability` arguably relates to station/map capability semantics.
- The permission (`dtms:facility:profile:*`) and admin page already exist.

**Cons:**
- Facility would grow a full asset-lifecycle subsystem (status machine, maintenance, location history, utilization projections) duplicating Fleet's.
- Contradicts the module's post-purge identity as static site topology.
- Different owners and change cadences end up in one module.

**Rejected because:** every argument for it addresses the *type* and none addresses the *instance*. It only wins if `Carrier` is never built, which is the entire point of the work.

### Alternative B: Binding as a child of the `Trip` aggregate in Dispatch

Model `TripCarriers` under `Trip`, with a real foreign key and cascade delete.

**Pros:**
- True referential integrity and cascade cleanup.
- Attach/detach guarded directly by `Trip.Status`; no projection, no staleness window.

**Cons:**
- Requires editing `Trip.cs`, the file with the highest concurrency risk in the system.
- Splits carrier data across two modules — the exact problem this ADR set out to remove.

**Rejected because:** the correctness benefit is real but small (a sub-second staleness window that a 60-second reconciler already backstops), while the risk is concentrated in the code path least tolerant of new writers.

### Alternative C: Fleet calls Dispatch synchronously through a published contract

Add an `ITripLookup` contract that `AttachCarrierToTrip` calls to validate trip state.

**Pros:**
- Always-fresh trip state; no projection table.

**Cons:**
- Introduces runtime coupling from Fleet to Dispatch that the module boundaries deliberately avoid.
- Auto-release on terminal states still needs event subscription — so the projection appears anyway, and now there is coupling *and* a projection.

**Rejected because:** it pays the coupling cost without removing the event subscription that motivated it.

### Alternative D: Hard-code the capture paths inside `AttachCarrierToTrip`

Branch on `TransportMode` inside a single handler.

**Pros:**
- Fastest to write; no new abstraction.

**Cons:**
- Every new transport method edits a central handler.
- Precedence rules get tangled with mode detection, making both hard to test.

**Rejected because:** [`DispatchStrategyRegistry`](../../../src/Modules/Dispatch/DTMS.Dispatch.Application/Services/DispatchStrategyRegistry.cs) already solves this exact shape for dispatch across three modes, and reusing the pattern costs one interface.

### Alternative E: Event-driven — anyone publishes a `CarrierBound` event

**Pros:**
- Maximum flexibility; no central arbiter.

**Cons:**
- Nothing decides which source wins, so conflicting bindings resolve by arrival order.
- Contradictions become silent data corruption instead of a rejected request.

**Rejected because:** requirement 4 needs multiple sources *with* a precedence rule; without the arbiter the flexibility is a liability.

### Alternative F: A dedicated eleventh module

**Rejected because:** four tables do not justify a new `DbContext`, migration-history participant, DI block, and five `.csproj` files — and it would still need the same Trip-event subscription.

## Implementation Details

### Schema

```
fleet."CarrierTypes"          -- moved from facility."CarrierTypeProfiles"
  Id · Code · DisplayName · AmrCapability · MaxWeightKg · MaxSlots · Description

fleet."Carriers"
  Id · CarrierCode varchar(50) · CarrierTypeId uuid FK→CarrierTypes(Id) RESTRICT
  Barcode · DisplayName · Status varchar(20)  -- Available|InUse|Maintenance|Retired
  MaintenanceReason · MaintenanceSince
  CurrentLocationCode · LastSeenAt · CurrentTripId uuid
  CommissionedAt · RetiredAt · RetireReason
  CreatedAt · CreatedBy · ModifiedAt · ModifiedBy · xmin
  UNIQUE(CarrierCode) · UNIQUE(Barcode) WHERE NOT NULL · IX(Status, CarrierTypeId)

fleet."CarrierMaintenanceLog"                 -- one row per repair episode
  Id · CarrierId FK→Carriers(Id) CASCADE · Reason
  StartedAt · StartedBy · EndedAt · EndedBy · Outcome
  IX(CarrierId, StartedAt DESC) · UNIQUE(CarrierId) WHERE EndedAt IS NULL

fleet."CarrierAssignments"
  Id · CarrierId FK→Carriers cascade · TripId uuid (no FK) · TripAttemptNumber
  CarrierCode ❄ · CarrierTypeCode ❄          -- snapshot at attach time
  Source varchar(20) · Confidence varchar(10)
  AttachedAt · AttachedBy · DetachedAt · DetachedBy · DetachReason
  UNIQUE(CarrierId) WHERE "DetachedAt" IS NULL
  IX(TripId) · IX(CarrierId, AttachedAt DESC)

fleet."CarrierLoads"
  Id · CarrierAssignmentId FK cascade · CarrierId · TripId
  ItemPk · ItemRef ❄ · DeliveryOrderId ❄ · OrderRef ❄ · WeightKg ❄
  LoadedAt · LoadedBy · UnloadedAt · UnloadedBy
  UNIQUE(ItemPk) WHERE "UnloadedAt" IS NULL

fleet."CarrierPlans"
  Id · DeliveryOrderId · PickupLocationCode · DropLocationCode
  RequestedCarrierCode · RequiredCarrierTypeCode
  Source · CreatedBy · CreatedAt · ConsumedAt
  CHECK (RequestedCarrierCode IS NOT NULL OR RequiredCarrierTypeCode IS NOT NULL)

fleet."ActiveTrips"                          -- projection, not a source of truth
  TripId PK · DeliveryOrderId · Status · AttemptNumber
  PickupLocationCode · DropLocationCode · UpdatedAt
```

The `❄` columns are snapshots frozen at write time, following `Trip.PickupLocationCode` and `Trip.TemplateNameAtDispatch`. They are what makes requirement 5 hold: retiring or renaming a carrier cannot rewrite history.

> **Amended during P1 implementation (2026-08-31).** Three details of the sketch above changed once the code was written. None reverses a decision in this ADR; they correct how it is realised.
>
> 1. **The carrier's FK targets `CarrierTypes.Id`, not `.Code`.** `Code` carries only a unique *index*, and EF requires an FK's principal to be a *key*. Declaring `HasPrincipalKey(t => t.Code)` would add an alternate key, which EF materialises as a second UNIQUE constraint — and therefore a second index — beside `IX_CarrierTypes_Code`. No other table in this solution uses `HasAlternateKey`. Callers still speak in carrier-type codes; handlers resolve them to ids.
> 2. **Maintenance keeps a `CarrierMaintenanceLog` rather than only a status + reason.** The operation is reactive — nothing is scheduled — so the full `MaintenanceRecord` shape Vehicle uses buys capability nobody needs. But history is the one thing that cannot be reconstructed later, so it is recorded from the first day at the cost of one table.
> 3. **Removal is two operations, and `Retired` is reversible.** `retire` keeps the row, its history, and its code reservation forever; `delete` destroys the row and frees the code, and is permitted only when the carrier has no history at all. That guard is what lets both rules stand together: anything with history cannot be deleted, so no historical statement about a code can ever become ambiguous. `un-retire` exists because without it a mis-clicked retirement is unrecoverable — it can neither return to service (that path starts from `Maintenance`) nor be deleted (that path requires `Available`).
>
> Carriers also carry `CreatedAt`/`CreatedBy`/`ModifiedAt`/`ModifiedBy` like `ActionTemplate`, the closest existing admin-managed catalogue, and `CommissionedAt` is nullable and backdatable — the date the cart entered service, distinct from when its row was created.

The two partial unique indexes carry the core invariants — a carrier belongs to at most one open trip, and an item sits on at most one carrier — enforced by Postgres rather than by application locking.

### Per-mode policy defaults

| Mode | AutoAllocate | RequireConfirmation | AllowScanOverride |
|---|---|---|---|
| `Amr` | yes | no (advisory) | yes |
| `Manual` | no | no (advisory) | yes |
| `SelfManaged` | no | no | yes |

These are data, not code: adjusting them does not require a deployment, and a new transport method means writing one `ICarrierBindingStrategy` and registering it.

### Vendor correlation for AMR

DTMS puts `carrier:{code}` into `Riot3OrderRequest.Tags` at dispatch. RIOT3 echoes `task.tags` on every notify frame, giving a free vendor-side confirmation channel without any change on the vendor side. This confirms *what DTMS asked for*, so the assignment stays `Planned` until a human or source system confirms it.

## Edge Cases & Failure Modes

### Edge Case 1: two operators scan the same carrier onto different trips

Scenario: two PWA clients race on `CART-0001`.

**Handling:**
- `UNIQUE(CarrierId) WHERE "DetachedAt" IS NULL` rejects the second write.
- The loser receives 409, not corrupted state.

### Edge Case 2: scan lands while the trip is being cancelled

Scenario: an operator attaches a carrier microseconds before `TripCancelled` reaches Fleet.

**Handling:**
- The attach succeeds against the then-current `ActiveTrips` row.
- The same `TripLifecycleConsumer` releases it when the event arrives — no stranded row.
- This is the accepted cost of Decision B, bounded by event latency (milliseconds) and backstopped by the reconciler.

### Edge Case 3: terminal event arrives before the attach commits

Scenario: out-of-order delivery leaves an assignment open on a finished trip.

**Handling:**
- `CarrierReconciliationService` sweeps every 60 seconds for carriers marked `InUse` whose `CurrentTripId` is absent from `ActiveTrips`, force-releases them, and increments `dtms_carrier_force_released_total`.
- Mirrors the proven `Riot3ReconciliationService` shape.

### Edge Case 4: duplicate integration events

Scenario: at-least-once delivery replays a terminal event.

**Handling:**
- `ProjectionInbox` de-duplication keyed on `(ProjectorName, EventId)` — releases and metrics fire once.

### Edge Case 5: reconciler and operator write the same carrier row

**Handling:**
- The `xmin` concurrency token raises `DbUpdateConcurrencyException`; the loser reloads and re-applies, as `VehicleGroup` already does.

### Edge Case 6: a lower-confidence source tries to overwrite a confirmed binding

Scenario: an auto-allocation runs after an operator already scanned.

**Handling:**
- The precedence guard rejects with 409. Confirmed physical truth is never overwritten by a plan.

### Edge Case 7: nobody records a carrier at all

Scenario: an AMR trip runs with no plan and no operator present.

**Handling:**
- Not an error. `dtms_trips_completed_without_carrier_total{mode}` counts it and the UI shows a "no carrier" chip.
- Deliberately advisory: a new feature must not become a blocker in the existing dispatch flow.

### Edge Case 8: clock skew between an operator device and the server

**Handling:**
- All timestamps are stamped server-side in UTC. Client-supplied times are not accepted.

## Consequences

### Positive

- ✓ "Which carrier ran this trip, and what was on it?" becomes a single indexed query, forwards and backwards.
- ✓ Carrier domain is contained in one module; Facility returns to being exactly maps and stations.
- ✓ `CarrierTypeCode` becomes a real foreign key once both tables share the `fleet` schema — previously impossible across modules.
- ✓ Dispatch is untouched by the entire build-out except one optional nullable column in a read projection.
- ✓ Adding a fourth transport method requires one strategy class and one DI registration.
- ✓ Six dead files and one empty table leave the codebase.

### Negative

- ✗ No referential integrity between `CarrierAssignments.TripId` and `dispatch.Trips`; orphan rows are possible and the reconciler exists to catch them.
- ✗ `fleet.ActiveTrips` duplicates state Dispatch already owns, and can lag.
- ✗ Moving `CarrierTypeProfile` forces a permission rename whose user-side grants live on an external auth service that no migration can reach — a manual step that must precede deployment.
- ✗ "Fleet" now contains something that is not part of the robot fleet, which needs the naming caveat above to stay legible.
- ✗ Coverage depends on capture discipline; advisory enforcement means incomplete data is possible by design.

### Neutral

- `Item.LoadUnitProfileCode` becomes officially free text — a change of documentation, not behaviour.
- The admin page moves from `/facility/profiles` to `/fleet/carrier-types`, creating the first real page under `/fleet` (its `left-rail` entries previously pointed at non-existent routes).
- Snapshot columns make carrier reports intentionally immune to later master-data edits, which is correct for audit but means a typo fixed today does not fix yesterday's reports.

## Acceptance Criteria

- [x] `LoadUnitProfile` catalogue removed; `Item.LoadUnitProfileCode` behaviour unchanged
- [ ] `CarrierTypeProfile` relocated to `fleet.CarrierTypes` with data intact and constraints renamed
- [ ] `Carrier` registry with CRUD, admin UI, and maintenance transitions
- [ ] `fleet.ActiveTrips` proven to receive events from every active transport mode for at least two days
- [ ] Attach / load / detach endpoints with both partial unique indexes enforced under concurrent test
- [ ] Auto-release on terminal states, idempotent against replayed events
- [ ] Reconciler sweep plus `dtms_carriers_stuck_in_use` and `dtms_trips_completed_without_carrier_total`
- [ ] `ICarrierBindingStrategy` registry rejecting duplicate modes at startup
- [ ] Carrier history endpoint distinguishing `Planned` from `Confirmed`

## Related ADRs

- [ADR-001](adr-001-multi-mode-transport-split.md) — establishes the module boundaries this decision honours by keeping Dispatch untouched
- [ADR-002](adr-002-facility-station-hierarchy.md) — defines Facility as site topology, the basis for moving carrier data out of it
- [ADR-003](adr-003-trip-extension-tables.md) — precedent for keeping mode-specific data off `Trip` core
- [ADR-017](adr-017-permission-naming-standard.md) — governs the `dtms:fleet:carrier*` permission codes introduced here

## References

- Existing code: [CarrierTypeProfile.cs](../../../src/Modules/Facility/DTMS.Facility.Domain/Entities/CarrierTypeProfile.cs)
- Existing code: [DispatchStrategyRegistry.cs](../../../src/Modules/Dispatch/DTMS.Dispatch.Application/Services/DispatchStrategyRegistry.cs) — the pattern `ICarrierBindingStrategy` copies
- Existing code: [VehicleStateHistoryProjectionStore.cs](../../../src/Modules/Fleet/DTMS.Fleet.Infrastructure/Projections/VehicleStateHistoryProjectionStore.cs) — the projection/inbox pattern reused
- Existing code: [Riot3NotifyPayload.cs](../../../src/Modules/Transport.Amr/DTMS.Transport.Amr/Models/Riot3NotifyPayload.cs) — evidence that RIOT3 reports no carrier identity
- Migration of record: [20260506082818_AddCarrierTypeAndLoadUnitProfiles.cs](../../../src/Modules/Facility/DTMS.Facility.Infrastructure/Migrations/20260506082818_AddCarrierTypeAndLoadUnitProfiles.cs)
