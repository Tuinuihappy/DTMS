# Carrier QR P4 — scan a carrier at order creation

> **Status**: 🔵 Not started — drafted 2026-09-24
> **Ask**: An operator scans a carrier's QR label while creating a delivery order, and that carrier is recorded against the order, visible and replaceable afterwards.
> **Kind / size**: feature · L, ~17h across 4 phases
> **Parent**: [`adr-019-carrier-tracking.md`](../multi-mode-transport/adr/adr-019-carrier-tracking.md) §"Amended (2026-09-14)" (lines 250-258) — the five write rules locked there are **not** reopened by this plan.
> **Predecessor**: `~/.claude/plans/implement-plan-fuzzy-cookie.md` §"แผนที่ 2" (Thai, 2026-09-18). This document supersedes it: same design, re-verified against the tree at `3711748f`, ported into `docs/plans/` with sizing, off-ramps and one correction (see Phase 1.3).
> **Assumptions**: "phase 4" means only the order-creation writer + read-back. Trip binding (`CarrierAssignments`, `ICarrierBindingStrategy`, `ActiveTrips`) stays out — it is ADR-019 P3 and does not exist yet.

## The change in one sentence

A delivery order can carry the operator's stated intent about *which physical cart it goes on*, captured by a barcode scan at the moment the order is created rather than reconstructed later from memory.

## Goal / non-goal

**Goal**: for any single-route delivery order, a scan during creation results in exactly one open row in `fleet.CarrierPlans`, and the order's detail drawer shows that carrier with its *live* status. Editing a Draft replaces the plan without accumulating rows; removing the chip deletes it.

**Non-goal**, all deliberate:

- **No automation.** Nothing reads a plan. No reservation, no RIOT3 hand-off, no dispatch-time check. This phase banks data; ADR-019 P3 consumes it.
- **No `ConsumedAt` writer.** The column exists because the partial unique index needs it. Its writer arrives with trip binding.
- **No carrier-*type* plans.** `RequiredCarrierTypeCode` gets a column and nothing writes it (ADR-019 keeps the OR-shape for later).
- **No multi-route orders.** An order whose items span more than one pickup/drop pair cannot be scanned — dispatch would split it into several trips and one plan could not say which.
- **No chip in the orders table.** That list is served from another module's projection; a column there needs an event that does not exist.

---

## Pre-flight (~25 min)

Read once before starting:

- [`adr-019-carrier-tracking.md`](../multi-mode-transport/adr/adr-019-carrier-tracking.md) lines 219-224 (the table sketch) and 250-258 (the five locked rules) — the sketch is what Phase 4 amends.
- [`src/Modules/Fleet/DTMS.Fleet.Domain/Entities/CarrierMaintenanceLog.cs`](../../src/Modules/Fleet/DTMS.Fleet.Domain/Entities/CarrierMaintenanceLog.cs) — `Entity<Guid>` at `:21`; this is the shape `CarrierPlan` copies, not `Carrier`.
- [`src/Modules/Fleet/DTMS.Fleet.Infrastructure/Data/FleetDbContext.cs`](../../src/Modules/Fleet/DTMS.Fleet.Infrastructure/Data/FleetDbContext.cs) — DbSets `:13-30`, the `CarrierMaintenanceLog` block `:169-188` (partial index idiom at `:187`), `xmin` idiom at `:166`.
- [`src/Modules/Fleet/DTMS.Fleet.Application/Commands/DeleteCarrier/DeleteCarrierCommand.cs`](../../src/Modules/Fleet/DTMS.Fleet.Application/Commands/DeleteCarrier/DeleteCarrierCommand.cs) — `:7-13` + `:21`: how a *non-fatal* outcome travels back, since `Result<T>` cannot carry one.
- [`src/DTMS.Api/Adapters/DeliveryOrderStatusReader.cs`](../../src/DTMS.Api/Adapters/DeliveryOrderStatusReader.cs) — the cross-module reader seam this plan copies for routes (`:13`, DI at `ModuleServiceRegistration.cs:636-637`).
- [`frontend/components/delivery-orders/create-dialog.tsx`](../../frontend/components/delivery-orders/create-dialog.tsx) — 1103 lines. `FormState` `:61-71`, `blankForm` `:104-120`, `formFromOrder` `:124-166`, `onCreated` type `:247`, the discarded `createOrder` result `:348`, the transport-mode reset `:501-521`.
- [`frontend/components/fleet/carrier-scan-dialog.tsx`](../../frontend/components/fleet/carrier-scan-dialog.tsx) — the `Resolution` union `:37-42` that must grow a sixth member.

**Estimate anchor**: `6c0f61a3` — carrier QR phases 1-3 (print a label, scan it in the registry, drop `Carriers.Barcode`), **25 files / 1167 insertions**. This plan is the same order of magnitude: heavier on the backend (a new table, three routes, a cross-module reader) and lighter on the frontend, because the scan dialog it needs already exists. ~17h is that commit's shape plus a migration.

### Decision check before coding

Getting these wrong costs 2-4h each.

| Decision | Default | When to pick the alternative |
|---|---|---|
| Where the scan lives in the form | On the **existing** "Pick & drop" step (step 2 of 2), above the item cards | Never add a third step for this. The dialog hardcodes two: `:251` (`0 \| 1`), `:403-407` (`/ 2`), `:410` (label ternary), `:428` (50%/100%), `canAdvance` `:313-338` — five edits for no gain |
| How the plan is written | `handleSubmit` stops discarding `createOrder`'s `{ id }` (`:348`), POSTs the plan, then calls a **widened** `onCreated({ orderId, carrierCode, planWarning, planError })` | If the 3 call-site edits (`orders-experience.tsx:952`, `:962`, `:978`) fight with in-flight work, keep `onCreated: () => void` and raise the toast inside the dialog. Costs the edit-mode drawer bounce its ordering guarantee |
| Which permission guards resolve-by-scan | Keep `GET /carriers/{code}` + `dtms:fleet:carrier:read` (`CarrierEndpoints.cs:120-124`) | If the order-creator group lacks `carrier:read`, add a resolve route inside the carrier-plans group under `DeliveryOrder.OrderRead` rather than granting the whole carrier registry to order clerks. See Open questions |
| `ICarrierPlanRepository` surface | Owns `AddAsync`, `Remove`, `SaveChangesAsync` | The sibling `ICarrierMaintenanceLogRepository` deliberately has **neither** `Remove` nor `SaveChangesAsync` (`CarrierMaintenanceLogRepository.cs:8-14`) because its handlers save through `ICarrierRepository`. Copy that only if a handler ever needs one transaction spanning both — clear-plan needs its own `Remove`, so it doesn't |
| Clearing a plan | Hard `DELETE` of the row | Soft-closing by stamping `ConsumedAt` would poison the column trip binding must read. Not an option; recorded here so nobody re-derives it |
| Route-set equality check | Compare **location codes** | There is no alternative before validation: `SetStationIds` is only called from `MarkAsValidated` (`DeliveryOrder.cs:576`), so a Draft has no station ids. Two codes resolving to one station make the check stricter than dispatch — an over-refusal, never a wrong pass. Comment it at the call site |

### Open questions

- [ ] **Does the group that creates delivery orders hold `dtms:fleet:carrier:read`?** — blocks Phase 3. Permissions come from central auth, so this cannot be answered from this repo. **The trap**: this developer's own account carries a wildcard grant, so the scan button will work perfectly in their own testing and 403 for everyone else. Ask a non-wildcard order clerk to load `/fleet/carriers` before building the UI. If they can't: take the decision-table alternative. Either way, make the 403 say *"you don't have permission to look up carriers"*, not *"scan failed"*.

---

## Phase 1 — P4a: the table and the entity (~4h)

### 1.1 `CarrierPlan` entity + `CarrierPlanSource` enum (~1h)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Domain/Entities/CarrierPlan.cs`
- New: `src/Modules/Fleet/DTMS.Fleet.Domain/Enums/CarrierPlanSource.cs`
- Tests: `tests/Modules/Fleet.UnitTests/CarrierPlanTests.cs` — pure-domain, imitating `CarrierTests.cs` (static factory helper at `:16-18`, xUnit + FluentAssertions, no substitutes)

Shape:

```csharp
// Entity<Guid>, NOT AggregateRoot: nothing subscribes to plan events, so it needs
// no DomainEvents and no b.Ignore(...) in the DbContext, and the outbox interceptor
// (DomainEventOutboxSaveChangesInterceptor.cs:37-41, OfType<IAggregateRoot>) never
// sees it. Same call as CarrierMaintenanceLog.cs:21.
public class CarrierPlan : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string OrderRef { get; private set; }            // ❄ frozen, so a warning can name the clashing order
    public string? PickupLocationCode { get; private set; }
    public string? DropLocationCode { get; private set; }
    public string? RequestedCarrierCode { get; private set; }   // ❄
    public string? RequiredCarrierTypeCode { get; private set; } // ❄ column exists; P4 never writes it
    public CarrierPlanSource Source { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }  // no writer until trip binding

    public static CarrierPlan ForOrderRequest(Guid orderId, string orderRef, string carrierCode,
                                              string? pickupCode, string? dropCode, string createdBy);

    /// Editing is a new intention, so CreatedAt/CreatedBy are restamped, not preserved.
    public void Replace(string carrierCode, string? pickupCode, string? dropCode, string by);
}

public enum CarrierPlanSource { OrderRequested, SourceSystem, TemplateDefault, Planner }
```

All four enum values are declared now; only `OrderRequested` has a writer. Codes go through `Carrier.NormalizeAndValidateCode` (`Carrier.cs:107`, static → `string`, throws `ArgumentException`) so a lowercase scan is accepted.

Tests: normalize a lowercase code · reject empty · reject when both carrier and type are absent · `Replace` keeps `Id` and restamps `CreatedAt`/`CreatedBy` · a fresh plan is open (`ConsumedAt is null`).

### 1.2 Repository + DbContext block (~1h)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Domain/Repositories/ICarrierPlanRepository.cs` — one interface per file, XML-doc on each non-obvious method, following `ICarrierRepository.cs:6`
- New: `src/Modules/Fleet/DTMS.Fleet.Infrastructure/Repositories/CarrierPlanRepository.cs` — ctor-injected `FleetDbContext`, expression-bodied members, `AsNoTracking()` on reads (`CarrierRepository.cs:36`, `:78`)
- Edit: `src/Modules/Fleet/DTMS.Fleet.Infrastructure/Data/FleetDbContext.cs` — `DbSet<CarrierPlan> CarrierPlans` beside `CarrierMaintenanceLogs` at `:22`; a new inline `modelBuilder.Entity<CarrierPlan>` block placed after the `CarrierMaintenanceLog` block (ends `:188`) and before `Attachment` (`:190`)
- Edit: `src/DTMS.Api/Modules/ModuleServiceRegistration.cs` — register right after `:262`, fully qualified on both sides like `:259-260`

The DbContext block is inline; Fleet has **zero** `IEntityTypeConfiguration` classes. Mirror `:187` for the filtered index and `:166` for `xmin`:

```csharp
b.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");   // model-only, no DDL
b.HasIndex(x => x.DeliveryOrderId).IsUnique().HasFilter("\"ConsumedAt\" IS NULL");
b.HasIndex(x => x.RequestedCarrierCode)
 .HasFilter("\"ConsumedAt\" IS NULL AND \"RequestedCarrierCode\" IS NOT NULL");
```

No FK to `DeliveryOrders` — it is another module's schema.

### 1.3 Hand-written migration + snapshot (~1.5h)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Infrastructure/Migrations/20260924100000_AddCarrierPlans.cs`
- Edit: `src/Modules/Fleet/DTMS.Fleet.Infrastructure/Migrations/FleetDbContextModelSnapshot.cs`

**Write both by hand.** `dotnet-ef` does not run on this solution's runtime, so there is no `migrations add` to regenerate the snapshot — that shortcut does not exist here.

`20260924100000` is free: the highest MigrationId in *any* module today is `20260911100000_DropCarrierBarcode`, and all eight DbContexts share `public.__EFMigrationsHistory`, so the id must be unique repo-wide, not just within Fleet. Copy the file shape from `20260911100000_DropCarrierBarcode.cs`: `[DbContext(typeof(FleetDbContext))]` + `[Migration(...)]` inline (`:36-38`), `#nullable disable`, an XML-doc rationale, a real `Down()`, and `schema: "fleet"` passed explicitly on every operation.

DDL, per ADR-019:219-224 plus the two additions this phase amends in:

```sql
UNIQUE ("DeliveryOrderId") WHERE "ConsumedAt" IS NULL
CHECK (num_nonnulls("RequestedCarrierCode", "RequiredCarrierTypeCode") >= 1)
IX ("RequestedCarrierCode") WHERE "ConsumedAt" IS NULL AND "RequestedCarrierCode" IS NOT NULL
```

`>= 1`, not `= 1`: it matches the ADR's OR and leaves room for "this cart, or failing that this type".

**Correction to the 2026-09-18 plan** — it says to insert the snapshot block "between `Carrier` and `CarrierMaintenanceLog`". That is wrong. Snapshot blocks are ordered by full CLR type name, and `CarrierMaintenanceLog` < `CarrierPlan` < `CarrierType`, so the block goes **at line 226**, between `CarrierMaintenanceLog` (ends `:224`) and `CarrierType` (`:226`). Add nothing to the relationships section at `:598-647` — there is no FK.

### Phase 1 acceptance

- `dotnet build DTMS.slnx -m:1` clean; `dotnet test tests/Modules/Fleet.UnitTests -m:1` green including the new `CarrierPlanTests`.
- `powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api` — it runs the migrator and only recreates `api`/`outbox-worker` if the migrator exits 0, so a bad migration shows up as "migrator did not exit 0", not as a half-migrated database.
- Against `localhost:5434`, from a `.sql` file rather than `psql -c` (PowerShell strips the quotes out of inline SQL): `fleet."CarrierPlans"` exists, both filtered indexes and the CHECK are present, and `20260924100000` is the newest row in `public.__EFMigrationsHistory`.

**Commit**: `feat(fleet): record which carrier an order is meant to go on`

---

## Phase 2 — P4b: commands, the cross-module reader, endpoints (~5h)

### 2.1 `IDeliveryOrderRouteReader` + adapter (~1.5h)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Application/Services/IDeliveryOrderRouteReader.cs` — the interface lives in the **consuming** module, as `IDeliveryOrderStatusReader` does in `Dispatch.Application/Services/`
- New: `src/DTMS.Api/Adapters/DeliveryOrderRouteReader.cs` — `internal sealed`, sends `GetDeliveryOrderQuery` through `ISender`, flattens the DTO so Fleet never sees DeliveryOrder's enums (pattern: `DeliveryOrderStatusReader.cs:24-29`)
- Edit: `src/DTMS.Api/Modules/ModuleServiceRegistration.cs` — beside `:636-637`, under the same composition-root-seam comment

```csharp
public sealed record OrderRouteSummary(
    string OrderRef, string Status, int ItemCount, int DistinctRoutePairs,
    string? PickupLocationCode, string? DropLocationCode);
```

Drop items missing a code *before* grouping, so the count matches what dispatch would actually split on.

### 2.2 `UpsertCarrierPlanCommand` (~2h)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Application/Commands/UpsertCarrierPlan/UpsertCarrierPlanCommand.cs` — command + handler in one file, validation inline
- Tests: `tests/Modules/Fleet.UnitTests/CarrierPlanHandlerTests.cs` — NSubstitute fakes of the repository interfaces, no EF; copy the `Mocks()` helper at `CarrierLifecycleHandlerTests.cs:297-304` and `NullLogger<T>.Instance` at `:193-196`

```csharp
public sealed record UpsertCarrierPlanCommand(Guid DeliveryOrderId, string RequestedCarrierCode)
    : IRequest<Result<CarrierPlanWriteResult>>;

// Warning rides inside T: Result<T> has no warning channel and its ctor forbids a
// message on success (Result.cs:11-12). Same device as DeleteCarrierResult
// (DeleteCarrierCommand.cs:21).
public sealed record CarrierPlanWriteResult(Guid PlanId, string RequestedCarrierCode,
                                            bool Created, string? Warning = null);
```

`Source` is **not** a field of the command — a field that exists can be sent; a field that doesn't cannot. The handler stamps `OrderRequested`.

Validation order, inline (Fleet is not registered with `AddValidatorsFromAssembly` — `Program.cs:278-279` — so a FluentValidation validator here would silently never run; the reason is already written out at `RegisterCarrierCommand.cs:40-42`):

1. order id non-empty
2. normalize the code via `Carrier.NormalizeAndValidateCode`, catching `ArgumentException` → `Result.Failure`
3. order exists
4. order not in a terminal state — a plan is a future intention
5. **exactly one route pair**; the message states how many trips the order would become. This is the real gate: the UI can be bypassed
6. carrier exists
7. `Retired` and `Maintenance` → refuse, with the same wording the scan dialog shows (`carrier-scan-dialog.tsx:118-137`). **`InUse` → succeed with a warning** (`CarrierStatus` is `{ Available, InUse, Maintenance, Retired }`, `CarrierStatus.cs:15-21`)
8. same carrier already planned on another open order → append a warning naming that `OrderRef`; do not refuse
9. upsert, then one `SaveChangesAsync`

Handler tests, one per decision so a failure names the rule: server-stamped `Source` · edit overwrites without adding a row · multi-route refused with the trip count · duplicate carrier succeeds with a warning naming the other order · `InUse` succeeds with a warning · `Retired` and `Maintenance` refused · terminal order refused · lowercase code normalized.

### 2.3 `ClearCarrierPlanCommand` + `GetCarrierPlanQuery` (~1h)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Application/Commands/ClearCarrierPlan/ClearCarrierPlanCommand.cs` — hard delete; silent success when there is nothing to remove
- New: `src/Modules/Fleet/DTMS.Fleet.Application/Queries/GetCarrierPlan/GetCarrierPlanQuery.cs` — resolves the carrier's **live** status, type and display name at read time, so a cart that went into maintenance after planning reads as in maintenance
- Tests: extend `CarrierPlanHandlerTests.cs` — clear removes the plan · clear on nothing is silent · the query reports live status

### 2.4 `CarrierPlanEndpoints.cs` (~30 min)

Files:

- New: `src/Modules/Fleet/DTMS.Fleet.Presentation/CarrierPlanEndpoints.cs`
- Edit: `src/DTMS.Api/Modules/ModuleEndpointRegistration.cs` — add `app.MapCarrierPlanEndpoints();` beside `MapCarrierEndpoints()` at `:23`

| Route | Permission |
|---|---|
| `PUT /api/v1/fleet/carrier-plans/{orderId:guid}` | `Permissions.DeliveryOrder.OrderWrite` |
| `GET /api/v1/fleet/carrier-plans/{orderId:guid}` | `Permissions.DeliveryOrder.OrderRead` |
| `DELETE /api/v1/fleet/carrier-plans/{orderId:guid}` | `Permissions.DeliveryOrder.OrderWrite` |

A separate file because these are the only Fleet routes guarded by another module's permissions — the precedent is `WmsLocationEndpoints.cs:49`, which uses `Permissions.DeliveryOrder.OrderRead` for the same reason: the real caller is the order form. Use `.RequirePermission(...)` per route (`RequirePermissionExtensions.cs:18-25`), as `CarrierEndpoints.cs:120-124` does — **no new permission code is created**, so `PermissionCatalogTests` is untouched. `PUT` returns a body, not 204, because the warning has to reach the caller. A double-submit surfaces as 409 without any bespoke catch — `ExceptionHandlingMiddleware.cs:140-141` already maps SQLSTATE 23505 to `409 Duplicate Value` (note the folder is `src/DTMS.Api/Middlewares/`, plural).

### Phase 2 acceptance

- `dotnet test DTMS.slnx` green.
- From inside the api container, all three routes without a token → 401 (which also proves they are mapped).
- Real path: create a single-route order → `PUT` a plan → `GET` it back → `PUT` a different carrier → `SELECT count(*)` = **1**. A two-route order → refused, message names the trip count. The same carrier on a second open order → written, with a warning naming the first `OrderRef`.

**Commit**: `feat(fleet): let an order say which carrier it wants, and refuse the ones it can't have`

---

## Phase 3 — P4c: proxy route, client, create dialog (~5h)

### 3.1 Next route handler + api client (~1h)

Files:

- New: `frontend/app/api/fleet/carrier-plans/[orderId]/route.ts` — GET/PUT/DELETE, validating the GUID before proxying. Copy `app/api/fleet/attachments/[id]/route.ts:16-18` exactly, including `isGuid` from `lib/api/attachment-owners.ts:16-20` and Next 16's awaited params (`:10-12`), then `proxyToBackend` (`lib/api/proxy-helpers.ts:31-39`)
- New: `frontend/lib/api/fleet-carrier-plans.ts` — `BASE` + `getJson`/`send` skeleton from `fleet-carrier-types.ts:48-80`, but the `res.status === 404 → null` idiom from `fleet-carriers.ts:101` for `getCarrierPlan`, since "this order has no plan" is an ordinary answer

**No `proxy.ts` change and no `check:routes` change.** Its matcher (`proxy.ts:80-93`) holds *page* prefixes only — `"/fleet/:path*"` is `app/fleet`, not `app/api/fleet` — and `scripts/check-route-protection.mjs:59` skips `api` outright, because API routes must answer 401 rather than redirect.

### 3.2 A sixth state for the scan dialog (~1h)

Files:

- Edit: `frontend/components/fleet/carrier-scan-dialog.tsx` — add a member to `Resolution` (`:37-42`) for *usable but flagged*, e.g. `{ kind: "found"; carrier: Carrier; warning?: string }`, and set it for `InUse` in `settle()` (`:115-140`) instead of falling through to bare `found` at `:138`
- Edit: the same file's confirm button (`:288-292`) — `disabled={!found}` must stay false for the flagged state

Today the union has exactly five members and `settle()` only rejects `Retired` (`:118-126`) and `Maintenance` (`:127-137`). `InUse` already lands in `found`, so this step is about *surfacing* the warning, not permitting the scan. Keep the existing `carriers-experience.tsx:409` consumer working.

### 3.3 Create dialog: scan button, chip, and the write (~2.5h)

Files:

- Edit: `frontend/components/delivery-orders/create-dialog.tsx` — `FormState` (`:61-71`) gains `carrier: { code, typeCode, displayName } | null`; add it to `blankForm` (`:104-120`) and `formFromOrder` (`:124-166`); scan button + chip at the top of the "Pick & drop" step, above the item cards; `handleSubmit` (`:340-364`) stops discarding `createOrder`'s `{ id }` at `:348`, POSTs the plan, and calls the widened `onCreated`
- Edit: `frontend/components/delivery-orders/orders-experience.tsx` — the three `onCreated` props at `:952`, `:962`, `:978`

Store the three resolved values, not the whole carrier DTO — holding the DTO invites reading a `status` that has since gone stale.

The route gate is a **disabled button with the reason showing**, never a hidden one: hiding it reads as "this feature doesn't exist". The chip only renders once the route set is valid, so the form never displays a carrier that wouldn't be saved.

**Do not clear the carrier when transport mode changes.** The block at `:501-521` wipes items' pickup/drop because those code sets are mode-specific; a carrier code is not. Once pickup/drop are empty the chip hides itself, and it reappears with the same carrier when a new route is picked — no second walk to the cart. Write that intent as a comment, because the neighbouring lines all clear things. *(This reverses a note in older session memory that said to clear it; the 2026-09-18 design settled on keeping it, and that reasoning stands.)*

Failure of the plan write is not failure of the order: the order is already committed, so carry the message out in `planError` and continue.

Toasts use the existing three tones — `success | error | info`, `toast.tsx:17`; there is no `warning` tone and this is not the moment to add one. Order: `error` if the plan write failed · `info` if it succeeded with a warning · `success` naming the carrier · plain `success` when nothing was scanned.

### Phase 3 acceptance

- `npx tsc --noEmit` clean (there is no eslint in this project, so the dependency arrays you touch get hand-checked) and `node scripts/check-route-protection.mjs` passes.
- Single-route order created with a scan → one toast naming the carrier, one row in `fleet.CarrierPlans`.
- Two-route order → button disabled, reason visible.
- Re-open that Draft → the chip shows the current plan; remove it and save → the row is gone.
- Change transport mode, pick a new route → the scanned carrier is still there.
- Scan an `InUse` carrier → accepted, with the warning shown.

**Commit**: `feat(orders): scan a carrier while creating an order, and see it on the draft`

---

## Phase 4 — P4d: read-back on the order drawer, and the ADR amendment (~2.5h)

### 4.1 "Planned carrier" section (~1.5h)

Files:

- Edit: `frontend/components/delivery-orders/detail-drawer.tsx` — a fourth soft-fail fetch in the effect at `:106-161`, following the timeline/trips/jobs pattern (`.then(setX)` guarded by `if (!cancelled)`, empty `.catch(() => {})`, section hides on failure); the section itself between Totals (`:277-295`) and Notes (`:302-310`), in the gap at `:297-300`

Contents: code, the carrier's **live** status, type, route, and who recorded it when — plus one line saying this is a stated intention and no carrier is bound to a trip yet. Not a chip in the header: it loads later than the header and would push the heading around.

### 4.2 Amend ADR-019 (~1h)

Files:

- Edit: `docs/multi-mode-transport/adr/adr-019-carrier-tracking.md` — a new amendment block in the same voice as the 2026-08-31 / 2026-09-11 / 2026-09-14 ones, and tick the checklist item at `:388`

Three things to record: the shipped table carries `OrderRef` (so a warning can name the clashing order without a cross-module read) and a model-only `xmin`; the CHECK reads `num_nonnulls(...) >= 1`; and `ConsumedAt` has no writer yet, which means the partial unique index currently behaves as a table-wide unique — correct for now, and the thing that changes when trip binding lands.

### Phase 4 acceptance

- A drawer for an order with a plan shows the section; one without shows nothing.
- Put that carrier into maintenance from the Carriers page, reopen the drawer → the status has changed. That is the proof the status is read live rather than snapshotted.

**Commit**: `feat(orders): show the carrier an order was planned onto, with its status today`

---

## Verification

| What | How | Where |
|---|---|---|
| Domain + handler rules | `dotnet test tests/Modules/Fleet.UnitTests -m:1` | `CarrierPlanTests`, `CarrierPlanHandlerTests` |
| Nothing else regressed | `dotnet test DTMS.slnx` (~1,140 baseline) | whole solution |
| Table, indexes, CHECK | a `.sql` file against `localhost:5434` — not `psql -c`, PowerShell strips the quotes | `fleet."CarrierPlans"`, `pg_indexes`, `pg_constraint` |
| Migration actually ran | newest row in `public.__EFMigrationsHistory` is `20260924100000` | same connection |
| Routes exist and are guarded | from inside the api container, all three verbs with no token → 401 | `/api/v1/fleet/carrier-plans/{guid}` |
| Upsert really replaces | `SELECT count(*)` after two `PUT`s = 1 | psql |
| Frontend types | `npx tsc --noEmit`, `node scripts/check-route-protection.mjs` | `frontend/` |
| Live status, not a snapshot | maintenance a planned carrier, reopen the drawer | UI |

No HTTP-level tests: the project has no harness for them, and `Fleet.UnitTests` references only `DTMS.Fleet.Application` and `DTMS.Fleet.Domain` — not Infrastructure — so nothing there can reach `FleetDbContext` either. Index and constraint behaviour is proven by psql, routes by the 401 probe. Building that harness is not in this plan's budget.

Nothing here is ops-facing, so there is no metric or alert to add.

## Docs to update when this lands

- `docs/multi-mode-transport/adr/adr-019-carrier-tracking.md` — the amendment in 4.2, and tick `:388`.
- No new ADR. Every decision either already sits in ADR-019:250-258 or is a realisation detail of it, which is what an amendment is for.
- This file: close-out block at the bottom, like `g1-signalr-drain-protocol.md`.

## Definition of done

- [ ] A single-route order created with a scan has exactly one open row in `fleet.CarrierPlans`; a two-route order cannot be scanned and says why
- [ ] Editing a Draft twice leaves one row, not two (`count(*)` = 1)
- [ ] Removing the chip deletes the row
- [ ] The order drawer shows the planned carrier's status *as of now*, proven by maintenancing it and reopening
- [ ] `Retired`/`Maintenance` refused with the same wording the scan dialog uses; `InUse` accepted with a warning
- [ ] `Source` is `OrderRequested` on every row, and the command has no field that could override it
- [ ] `dotnet test DTMS.slnx` green; `npx tsc --noEmit` clean
- [ ] ADR-019 amended and its checklist item ticked
- [ ] Effort stayed within ~17h (overshoot past ~26h = stop and re-scope)

## Off-ramps

- **If the hand-written snapshot edit in 1.3 exceeds 1.5h**: stop hand-diffing the whole file. Add the entity block only, run the migrator, and prove the shape from psql instead of from the snapshot — then reconcile the snapshot against the migration column by column in a separate sitting. A wrong snapshot corrupts *future* migrations, so it must be right, but it does not have to be right in the same hour.
- **If the order-creator group lacks `dtms:fleet:carrier:read`** (Open questions): don't stall on an auth-service request. Take the decision-table alternative — a resolve route inside the carrier-plans group under `OrderRead`. Roughly +1h, and it removes an external dependency permanently.
- **If widening `onCreated` fights with in-flight work** in `orders-experience.tsx`: keep `onCreated: () => void`, raise the toast inside the dialog, and ship Phases 1-3 minus the drawer-bounce ordering. The plan still gets written; only toast composition gets less tidy.
- **If Phase 3 runs long**: ship Phases 1, 2 and 4 and stop. Backend plus read-back is a coherent release — plans can be written by API and shown on the drawer, with the scan button following. Phase 4 does not depend on Phase 3.
- **If the route-pair check turns out to be hard to compute in the dialog**: let the button stay enabled and rely on the server's gate (step 5 of 2.2), showing the refusal as an `error` toast. Worse UX, same data integrity, ~2h saved.

## Risks & rollback

| Risk | Likelihood | Mitigation / rollback |
|---|---|---|
| Hand-edited snapshot drifts from the migration | Medium | Compare column by column against the migration before committing; a wrong block breaks the *next* migration, not this one. Rollback = `Down()` in `20260924100000_AddCarrierPlans` drops the table |
| Order creators lack `carrier:read`, and the wildcard grant hides it during testing | Medium | The Open question, checked with a non-wildcard account before Phase 3; off-ramp is a ready alternative |
| Route check uses codes while dispatch uses resolved ids | Low | Strictly an over-refusal. Commented at the call site. Disappears if Draft ever gains station ids |
| Widening `onCreated` misses a call site | Low | `tsc --noEmit` catches it; there are exactly three (`:952`, `:962`, `:978`) |
| The plan-load effect races the form-reset effect on edit | Medium | Declare it after the reset effect at `:263-284` and use the functional form of `setForm`, or it overwrites what the user typed |
| Plans pile up on dead orders | Low | Accepted per ADR-019. Nothing reads them, so nothing is wrong today; a future "open plans" count will drift upward and will need a sweep then |
| Someone reads this phase as automation | Medium | Every new file carries a P4 comment naming trip binding as the consumer |

## What would invalidate this plan

- **Trip binding (ADR-019 P3) landing first.** Then `ConsumedAt` gets a writer, the partial unique index stops behaving as a table-wide unique, and both the query and the amendment in 4.2 need rewriting.
- **A Draft gaining station ids.** `SetStationIds` is reachable only from `MarkAsValidated` today (`DeliveryOrder.cs:576`); if that changes, the route check should compare ids and the over-refusal caveat goes away.
- **`Result<T>` gaining a warning channel.** `CarrierPlanWriteResult.Warning` exists only because it hasn't got one (`Result.cs:11-12`).

## When this plan is picked up

**First move**: open `src/Modules/Fleet/DTMS.Fleet.Domain/Entities/CarrierMaintenanceLog.cs`, read the `Entity<Guid>` declaration at `:21` and the class doc above it, then write `CarrierPlan.cs` beside it with the same shape.

The pre-flight names the seven files worth holding in your head; the decision table has every fork already defaulted, and the only genuinely open item is the carrier-read permission, which is worth ten minutes with a non-wildcard account before Phase 3 rather than an hour of rework after it. Two focused days from cold start. Phases 1, 2 and 4 are independent of Phase 3, so a bad week can still ship most of this.
