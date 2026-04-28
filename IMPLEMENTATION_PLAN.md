# Production Implementation Plan

> Based on `PRODUCTION_READINESS.md` last updated 2026-04-29.
> Current state: Phase 0–2 complete. Phase 3–5 remaining.
> Tests: 54 unit + 20 integration = 74 total passing.

## Executive Summary

Phases 0–2 and pre-Phase 3 bug fixes are complete.

**Completed blockers (2026-04-29):**
1. ~~H3 Multi-tenancy~~ — ITenantContext, EF query filters, JWT tenant claim, cross-tenant isolation tests
2. ~~EF migrations~~ — InitialCreate + AddTenantId + FixTenantIndexes scaffolded and verified for all 8 DbContexts
3. ~~Build/runtime bugs~~ — station persistence (MapRepository), Trip Legs format, VehicleType seeding, migration gaps

**Remaining work (2–3 weeks):**
1. Phase 3 — Integration test scenarios (8 readiness cases still pending)
2. Phase 4 — Load/stress testing against 500 orders/min NFR
3. Phase 5 — Release gate evidence

## Production Definition Of Done

| Criteria | Status |
|---|---|
| Production starts with EF migrations, not `EnsureCreated` | ✅ Done — all 8 contexts use `MigrateAsync()`; Production guard throws if no migrations |
| Tenant-owned data isolated at API, repository, EF query filter, event, consumer | ✅ Done — query filters on 5 aggregate roots; events carry TenantId; consumers set tenant context |
| Cross-tenant reads/writes covered by automated tests | ✅ Done — 5 cross-tenant isolation tests in `TenantIsolationTests.cs` |
| Readiness integration scenarios run in CI without manual DB setup | ⏳ Partial — harness ready, 20 tests pass; 8 specific readiness scenarios pending (Phase 3) |
| Staging-like load test proves target throughput | ❌ Pending — Phase 4 |
| No production secrets committed or defaulted | ✅ Done — H1 fixed; docker-compose has dev placeholder only |
| `/health` and `/health/ready` reflect process and dependency health | ✅ Done — M2 fixed |
| Deployment, rollback, backup, restore, vendor-contract checks have evidence | ❌ Pending — Phase 5 |

---

## ~~Phase 0 - Baseline Verification~~ ✅ Done 2026-04-29

**Results:**
- 54/54 unit tests pass (was 44, 10 new after TspSolverTests fixed)
- 20/20 Testcontainer integration tests pass (harness was broken before)
- `/health` → 200, `/health/ready` → 200, `/swagger` → 200
- All 9 DB schemas created on fresh Docker start

**Bugs fixed during baseline:**
- `NuGetAuditSuppress` for `System.Security.Cryptography.Xml` 9.0.0 (design-time CVE, not runtime)
- `InternalsVisibleTo("Fleet.Infrastructure")` for `VehicleGroup.LoadVehicleIds`
- `Planning.Infrastructure.csproj` → add `Facility.Infrastructure` reference
- `Riot3RouteModels.cs` → `internal` → `public` (interface return type mismatch)
- `Riot3Webhooks.cs` → remove `async` from lambda with no `await` (CS1998)
- `VendorAdapterDbContext.cs` → remove `Ignore(DomainEvents)` on `Entity<Guid>`
- `Program.cs` → fix EnsureCreated when DB pre-created by `POSTGRES_DB` env var
- `TspSolverTests.cs` → replace `SimpleRouteCostCalculator` with `FlatCostCalculator` stub

---

## ~~Phase 1 - EF Migrations For Production~~ ✅ Done 2026-04-29

**All 8 DbContexts have migrations:**

```
src/AMR.DeliveryPlanning.Api/Auth/Migrations/
  20260428142845_InitialCreate
  20260428145806_AddTenantId        ← empty (captured later in FixTenantIndexes)
  20260428215055_FixTenantIndexes   ← drops IX_Username, adds TenantId column + IX_TenantId_Username

src/AMR.DeliveryPlanning.Api/Infrastructure/Outbox/Migrations/
  20260428142846_InitialCreate

src/Modules/DeliveryOrder/.../Migrations/
  20260428142834_InitialCreate
  20260428144758_AddTenantId
  20260428215054_FixTenantIndexes   ← drops IX_OrderKey, creates IX_TenantId_OrderKey

src/Modules/Dispatch/.../Migrations/
  20260428142837_InitialCreate
  20260428144800_AddTenantId

src/Modules/Facility/.../Migrations/
  20260428142823_InitialCreate

src/Modules/Fleet/.../Migrations/
  20260428142833_InitialCreate
  20260428144801_AddTenantId        ← Vehicle.TenantId + VehicleGroup.TenantId

src/Modules/Planning/.../Migrations/
  20260428142836_InitialCreate
  20260428144759_AddTenantId

src/Modules/VendorAdapter/.../Migrations/
  20260428142838_InitialCreate
```

**Production guard in `Program.cs`:**
```csharp
else if (env.IsProduction())
    throw new InvalidOperationException(
        $"Production startup aborted: {dbName} has no EF migrations.");
```

**Run migrations (macOS/Linux):**
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5434;Database=amr_delivery_planning;..."

# Example — repeat for each module
dotnet ef migrations add <MigrationName> \
  --context FacilityDbContext \
  --project src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations
```

---

## ~~Phase 2 - Multi-Tenancy~~ ✅ Done 2026-04-29

### 2.1 Tenant Context ✅

- `ITenantContext` (read-only) + `TenantContext` (scoped, `Set(Guid)`)
- `TenantContextMiddleware` — reads `User.FindFirstValue("tenant_id")` after `UseAuthorization`
- `builder.Services.AddScoped<TenantContext>()` + `AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>())`

### 2.2 Domain & Persistence ✅

**Tenant-owned (TenantId added):** DeliveryOrder, Job, Trip, Vehicle, VehicleGroup

**Shared/global (no TenantId):** Map, Station, RouteEdge, Zone, TopologyOverlay, FacilityResource, VehicleType, ChargingPolicy, ActionCatalogEntry, CostModelConfig

**Constructors:** all require `tenantId` as first parameter; 8 command handlers inject `ITenantContext`

### 2.3 EF Query Filters ✅

```csharp
// In each tenant-scoped DbContext constructor:
public XyzDbContext(DbContextOptions<XyzDbContext> options, ITenantContext tenantContext)

// In OnModelCreating:
builder.HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
```

Applied to: DeliveryOrder, Job, Trip, Vehicle, VehicleGroup

`IgnoreQueryFilters()` used only in `TripRepository.GetTripByTaskIdAsync` — RIOT3 webhook has no tenant, resolves tenant from found trip.

### 2.4 Events, Outbox, Consumers ✅

- `TenantId` added to: `DeliveryOrderReadyForPlanningIntegrationEvent`, `PlanCommittedIntegrationEvent`, `TripCompletedIntegrationEvent`
- Consumers: `DeliveryOrderValidatedConsumer`, `PlanCommittedConsumer`, `Riot3TaskCompletedConsumer`, `Riot3TaskFailedConsumer` all call `_tenantContext.Set(tenantId)` before DB access

### 2.5 API & Auth ✅

- `AppUser.TenantId` + `AppUser.SystemTenantId = new Guid("00000000-0000-0000-0000-000000000001")`
- JWT claims: `tenant_id` added to issued tokens
- Register endpoint: reads `X-Tenant-Id` header
- `AuthHelper.GetClientForTenantAsync(tenantId)` — registers + authenticates per-tenant client
- `TenantIsolationTests.cs` — 5 tests: order, list, trip, vehicle, job cross-tenant isolation

---

## Pre-Phase 3 Bug Fixes ✅ Done 2026-04-29

Discovered when running Testcontainer integration tests for the first time.

| Bug | Root Cause | Fix |
|---|---|---|
| Station not persisted | `b.Ignore(m => m.Stations)` prevents EF from tracking Map's Stations navigation. `AddStationCommandHandler` adds to in-memory collection only. | `MapRepository.Update`: iterate `map.Stations`, `_dbContext.Stations.Add(s)` for detached entries |
| Trip creation 500 (NullRef) | Tests sent `PickupStationId/DropStationId` but `DispatchTripCommand` requires `Legs` list | Add `DtmsWebApplicationFactory.BuildSingleLeg()` helper; update tests |
| VehicleType not found | Tests used `VehicleTypeId = Guid.NewGuid()` (doesn't exist in DB); no HTTP endpoint to create VehicleType | Add `DtmsWebApplicationFactory.CreateVehicleTypeAsync()` — inserts VehicleType directly via EF |
| List returns `[]` | `GetDeliveryOrdersQuery` defaults to `OrderStatus.Submitted`; orders are `ReadyToPlan` after submit flow | Update test query to `?status=ReadyToPlan` |
| Migration pending changes warning | `AddTenantId` migration captured column add but not index change | Scaffold `FixTenantIndexes` for DeliveryOrderDbContext + AuthDbContext |

---

## Phase 3 - Integration Test Completion

**Objective:** automate the 8 readiness scenarios that remain unchecked.

**Current state:** harness is ready, 20 basic tests pass. Need 8 specific scenario tests.

**Use Existing Harness**

- [DtmsWebApplicationFactory.cs](tests/Integration/AMR.DeliveryPlanning.IntegrationTests/DtmsWebApplicationFactory.cs) — PostgreSQL Testcontainers, in-memory Redis, `CreateVehicleTypeAsync()`, `CreateStationPairAsync()`, `BuildSingleLeg()`
- [AuthHelper.cs](tests/Integration/AMR.DeliveryPlanning.IntegrationTests/AuthHelper.cs) — `GetAuthenticatedClient()`, `GetClientForTenantAsync(tenantId)`

**Pending Test Cases**

1. **E2E full pipeline** — Submit order → Auto-plan (DeliveryOrderValidatedConsumer) → Auto-dispatch (PlanCommittedConsumer) → RIOT3 task complete → order marked Completed
   > Note: requires MassTransit in-process bus or simulated consumer trigger

2. **RIOT3 webhook behavior:**
   - `taskEventType=finished` → trip task marked complete, next task dispatched
   - `taskEventType=failed` → exception raised, error code recorded
   - Unknown taskId → safe 200/warning log, no crash

3. **Idempotency:**
   - Duplicate `POST /api/delivery-orders` with same `Idempotency-Key` → same orderId
   - Same body, different key → new orderId

4. **SLA validation:**
   - SLA < 30 min from now → `BadRequest`
   - Valid SLA → `ReadyToPlan` status

5. **Charging policy:**
   - `PUT /api/fleet/vehicles/{id}/state` with `BatteryLevel < threshold` → `VehicleBatteryLowIntegrationEvent` written to outbox

6. **Outbox processor:**
   - Domain event write → outbox row exists
   - Outbox processor picks up → marks `ProcessedAt`
   - On error → row remains retryable

7. **Capability assignment:**
   - Vehicle registered with `LIFT` capability
   - Job created with `RequiredCapability = "LIFT"`
   - Only `LIFT` vehicle gets assigned

8. **Amendment and timeline:**
   - `PATCH /api/delivery-orders/{id}` → amendment record created
   - `GET /api/delivery-orders/{id}/timeline` → includes amendment event

**Acceptance Criteria**

- All 8 scenarios automated in `tests/Integration/AMR.DeliveryPlanning.IntegrationTests/`
- Tests run via `dotnet test` with no external dependencies beyond Testcontainers
- Tests fail if EF query filters are removed (tenant isolation regression)

**Estimate:** 4–6 days

---

## Phase 4 - Load And Stress Testing

**Objective:** prove the 500 orders/minute NFR before launch.

**Tasks**

- Add `k6` scripts in `tests/load/`:
  - `setup.js` — auth, seed map/stations/vehicle type/vehicle
  - `submit_burst.js` — burst order submission
  - `mixed.js` — planning + assignment + dispatch mix
  - `webhook_callbacks.js` — simulate RIOT3 task completion callbacks
- Capture: p50/p95/p99 latency, error rate, DB CPU/locks, Redis hit rate, outbox backlog, RabbitMQ queue depth
- Run: smoke (1 min), soak (30–60 min), spike (2× target)
- Document bottlenecks and tuning values

**Acceptance Criteria**

- ≥ 500 orders/min with p95 < 500ms and error rate < 0.1%
- Outbox backlog stays bounded
- DB connection pool stable
- Rate limiting (100 req/min) does not interfere with load test design

**Estimate:** 2–3 days

---

## Phase 5 - Release Gate

**Objective:** checklist with owner/date/evidence, not a judgment call.

**Checklist**

- [ ] Secrets via env vars or Vault/KMS: `Jwt:Secret`, `ConnectionStrings:DefaultConnection`, `RabbitMq:*`, RIOT3 credentials
- [ ] Readiness check covers all required dependencies (add RabbitMQ check)
- [ ] Logs, traces (Jaeger/OTLP), metrics visible in target environment
- [ ] PostgreSQL backup and restore procedure tested
- [ ] Rollback procedure documented and tested
- [ ] RIOT3 `Riot3RouteModels.cs` response shape verified against vendor spec
- [ ] Action catalog verified against RIOT3 vendor spec (22 actions, 2 vehicle types)
- [ ] Deployment runbook includes `dotnet ef database update` step
- [ ] `PRODUCTION_READINESS.md` updated: final status, test evidence, load-test evidence, owner/date per gate

**Estimate:** 1–2 days

---

## Execution Sequence

| # | Phase | Status |
|---|---|---|
| 0 | Baseline verification | ✅ Done |
| 1 | EF migrations | ✅ Done |
| 2 | Multi-tenancy | ✅ Done |
| Pre | Bug fixes before integration tests | ✅ Done |
| **3** | **Integration test scenarios** | **Next** |
| 4 | Load/stress testing | Pending |
| 5 | Release gate | Pending |

## Rough Timeline

| Phase | Estimate | Status |
|---|---:|---|
| ~~Baseline verification~~ | ~~0.5 day~~ | ✅ Done |
| ~~EF migrations~~ | ~~1–2 days~~ | ✅ Done |
| ~~Multi-tenancy~~ | ~~4–7 days~~ | ✅ Done |
| ~~Pre-Phase 3 bug fixes~~ | ~~0.5 day~~ | ✅ Done |
| **Integration tests** | **4–6 days** | **Next** |
| Load/stress testing | 2–3 days | Pending |
| Release gate | 1–2 days | Pending |
| **Remaining total** | **~1.5 weeks** | |

## Immediate Next Actions (Phase 3)

1. Set up in-process MassTransit bus in `DtmsWebApplicationFactory` (or use OutboxEventBus with manual pump) to enable E2E consumer-driven flow in tests.
2. Write `EndToEndPipelineTests.cs` — full Submit → Plan → Dispatch → Complete flow.
3. Write `Riot3WebhookTests.cs` — finished/failed/unknown scenarios.
4. Write `IdempotencyTests.cs`, `SlaValidationTests.cs`, `OutboxTests.cs`, `CapabilityTests.cs`, `AmendmentTests.cs`.
5. Run all 8 new tests + existing 20 = 28+ integration tests passing.
6. Proceed to Phase 4 (k6 load scripts) once all integration tests green.
