# Production Readiness & Implementation Status

> **Last Updated:** 2026-04-29
> **Build:** Passing | **Tests:** 54 unit + 20 integration = **74 total**
> **State:** Staging-ready prototype — NOT production-ready
> **Remaining:** Phase 3 (integration tests) → Phase 4 (load) → Phase 5 (release gate)

---

## Production Definition of Done

| Criteria | Status |
|---|---|
| Production starts with EF migrations, not `EnsureCreated` | ✅ Done — all 8 contexts use `MigrateAsync()`; Production guard throws if no migrations |
| Tenant-owned data isolated at API, EF query filter, event, consumer | ✅ Done — query filters on 5 aggregate roots; events carry TenantId |
| Cross-tenant reads/writes covered by automated tests | ✅ Done — 5 cross-tenant isolation tests pass |
| Readiness integration scenarios run in CI without manual DB setup | ⏳ Partial — harness ready, 20 tests pass; 8 specific scenarios pending (Phase 3) |
| Staging-like load test proves target throughput | ❌ Pending — Phase 4 |
| No production secrets committed or defaulted | ✅ Done — H1 fixed; docker-compose has dev placeholder only |
| `/health` and `/health/ready` reflect process and dependency health | ✅ Done — M2 fixed |
| Deployment, rollback, backup, restore, vendor-contract checks with evidence | ❌ Pending — Phase 5 |

---

## Phase Progress

| Phase | งาน | สถานะ |
|---|---|---|
| P1–P8 | Feature sprints (RIOT3, Outbox, Fleet, Facility, DeliveryOrder, Multi-vendor, Advanced) | ✅ Done |
| **Hardening 0** | Baseline fixes — build errors, health endpoints, EnsureCreated, doc drift | ✅ Done 2026-04-29 |
| **Hardening 1** | EF Migrations — 8 DbContexts scaffolded, MigrateAsync path, production guard | ✅ Done 2026-04-29 |
| **Hardening 2** | Multi-tenancy — ITenantContext, query filters, events, consumers, JWT tenant claim | ✅ Done 2026-04-29 |
| **Pre-Phase 3** | Integration test blockers — station save, Trip Legs, VehicleType, migration gaps | ✅ Done 2026-04-29 |
| **Phase 3** | Integration test scenarios (8 readiness cases) | **⏳ Next** |
| **Phase 4** | Load/stress testing — 500 orders/min via k6 | ❌ Pending |
| **Phase 5** | Release gate — secrets, runbook, vendor contract, backup/restore | ❌ Pending |

---

## ✅ Critical Fixes (C1–C5) — All Done 2026-04-28

### C1. RIOT3 Task Consumer หา Trip ไม่เจอ

- `ITripRepository.GetTripByTaskIdAsync(Guid taskId)` — lookup ผ่าน `RobotTasks` → `TripId`
- ใช้ `IgnoreQueryFilters()` เพราะ RIOT3 webhook ไม่มี tenant claim; set `TenantContext` จาก trip ที่พบ
- Consumers: `Riot3TaskCompletedConsumer`, `Riot3TaskFailedConsumer`

### C2. VehicleGroupRepository — Comma-Separated VehicleIds

**Root cause:** 4 problems พร้อมกัน — ไม่มี FK, race condition (lost update), ไม่มี index, ข้อมูลซ้ำ 2 ที่

**Fix:** normalize เป็น join table `fleet.VehicleGroupMembers`
- Composite PK `(VehicleGroupId, VehicleId)`, FK cascade, Index บน VehicleId
- `xmin` PostgreSQL system column เป็น optimistic-concurrency token
- `GetAllAsync` — single query ไม่มี N+1

### C3. Route Cost เป็น Pseudo-distance

**Fix:** Hybrid architecture:
```
Planning → CachedRouteCostCalculator (Redis 15s TTL)
                     ↓ cache miss
         SimpleRouteCostCalculator → facility.RouteEdges (DB)
                                               ↑ sync ทุก 30 min
                                      RouteEdgeSyncService (BackgroundService)
                                               ↑
                                      RIOT3 /api/v4/route/costs/...
```
> **Note:** `Riot3RouteModels.cs` response shape เป็น approximation — verify กับ vendor spec ก่อน go-live

### C4. Distance to Pickup Hardcoded = 10.0

- `FleetVehicleProvider`: คำนวณจาก `vehicle.CurrentNodeId` จริง via `IRouteCostCalculator`; `Task.WhenAll` แทน sequential
- `GreedyVehicleSelector`: ลบ `_cachedVehicles` static list (thread-unsafe), ลบ in-memory fallback

### C5. SubmitDeliveryOrder ไม่เรียก MarkAsValidated

```
Submit → Submitted → MarkAsValidated(pickup, drop) → Validated → MarkReadyToPlan → ReadyToPlan
```
- `IStationLookup.ExistsAsync` ตรวจ station ใน FacilityDB ก่อน
- `order.MarkAsValidated()` + `order.MarkReadyToPlan()` เรียกแน่นอน

---

## ✅ Security Hardening (H1–H3) — All Done

### H1. JWT Secret และ Credentials Hardcoded — Fixed 2026-04-28

- `appsettings.json` ล้าง plaintext secrets
- `Program.cs`: throw `InvalidOperationException` ถ้า `Jwt:Secret` ไม่ถูก set
- `docker-compose.yml`: `Jwt__Secret=dev-only-secret-min-32-chars-placeholder!` (dev เท่านั้น)

```bash
# Dev setup (ครั้งเดียวต่อ machine)
dotnet user-secrets set "Jwt:Secret" "<strong-random-key-min-32-chars>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5434;..."
dotnet user-secrets set "RabbitMq:Password" "<secret>"
```

### H2. In-Memory State หายเมื่อ Pod Restart — Fixed 2026-04-28

| Component | เดิม | แก้เป็น |
|---|---|---|
| `InMemoryActionCatalogService` | Process memory | `vendoradapter.ActionCatalogEntries` (DB) |
| `InMemoryCostModelService` | Process memory (Singleton) | `planning.CostModelConfigs` (DB, Scoped, write-through) |
| `VendorAdapterFactory._vehicleAdapterMap` | Static dict | `fleet.Vehicles.AdapterKey` column |

### H3. ไม่มี Multi-Tenancy — Fixed 2026-04-29

**Scope decisions:**
- Tenant-owned: `DeliveryOrder`, `Job`, `Trip`, `Vehicle`, `VehicleGroup`
- Shared/global: Facility (Maps, Stations, RouteEdges), VendorAdapter (ActionCatalog), VehicleType

**Implementation:**

```csharp
// SharedKernel
public interface ITenantContext { Guid TenantId { get; } }
public sealed class TenantContext : ITenantContext { public void Set(Guid id) => TenantId = id; }

// Middleware (after UseAuthorization)
var claim = context.User.FindFirstValue("tenant_id");
if (Guid.TryParse(claim, out var id)) tenantContext.Set(id);

// DbContext (per tenant-scoped context)
builder.HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);

// RIOT3 webhook — no tenant in payload
var trip = await _tripRepo.GetTripByTaskIdAsync(taskId);  // IgnoreQueryFilters()
_tenantContext.Set(trip.TenantId);
```

**Integration events:** `TenantId` added to `DeliveryOrderReadyForPlanningIntegrationEvent`, `PlanCommittedIntegrationEvent`, `TripCompletedIntegrationEvent`

**Auth:** `AppUser.TenantId`; JWT issues `tenant_id` claim; `X-Tenant-Id` header for register

**Tests:** `TenantIsolationTests` — 5 tests verify Tenant A cannot read/mutate Tenant B's order/trip/vehicle/job

---

## ✅ Operational Fixes (M1–M5)

### M1. EF Migrations — Fully Done 2026-04-29

| Context | Schema | Migrations |
|---|---|---|
| FacilityDbContext | `facility` | InitialCreate |
| FleetDbContext | `fleet` | InitialCreate, AddTenantId |
| DeliveryOrderDbContext | `deliveryorder` | InitialCreate, AddTenantId, FixTenantIndexes |
| PlanningDbContext | `planning` | InitialCreate, AddTenantId |
| DispatchDbContext | `dispatch` | InitialCreate, AddTenantId |
| VendorAdapterDbContext | `vendoradapter` | InitialCreate |
| AuthDbContext | `auth` | InitialCreate, AddTenantId, FixTenantIndexes |
| OutboxDbContext | `outbox` | InitialCreate |

**Production guard:**
```csharp
else if (env.IsProduction())
    throw new InvalidOperationException($"Production startup aborted: {dbName} has no EF migrations.");
```

**Add new migration:**
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5434;Database=amr_delivery_planning;..."
dotnet ef migrations add <Name> \
  --context <ContextName> \
  --project src/Modules/<Module>/<Module>.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations
```

### M2. Health Checks — Fixed 2026-04-28

| Endpoint | พฤติกรรม |
|---|---|
| `GET /health` | Liveness — 200 ถ้า process alive (anonymous) |
| `GET /health/ready` | Readiness — ตรวจ postgres + redis (tag: `ready`) |

### M3. Integration Tests — Infrastructure Complete, Scenarios Pending

**✅ พร้อมแล้ว:**
- Testcontainer harness (`DtmsWebApplicationFactory`, PostgreSQL, in-memory Redis)
- Multi-tenant JWT helper (`GetClientForTenantAsync`)
- `CreateVehicleTypeAsync()`, `CreateStationPairAsync()`, `BuildSingleLeg()` helpers
- 20 integration tests pass (basic E2E + cross-tenant isolation)

**⏳ ยังค้าง (Phase 3):**
- [ ] E2E full pipeline: Submit → Auto-plan → Auto-dispatch → RIOT3 complete
- [ ] RIOT3 webhook: `finished`/`failed`/unknown taskId
- [ ] Idempotency key: duplicate POST → same orderId
- [ ] SLA validation: < 30 min → 400 BadRequest
- [ ] ChargingPolicy: battery < threshold → `VehicleBatteryLowIntegrationEvent`
- [ ] Outbox processor: write → ProcessedAt marked; failure → retryable
- [ ] Capability assignment: LIFT job → only LIFT vehicle assigned
- [ ] Amendment + timeline: PATCH → amendment record → timeline event

### M4. Rate Limiting — Fixed 2026-04-28

Global fixed-window: **100 req/min** per client IP → HTTP `429`

### M5. Feeder Adapter Endpoints — Fixed 2026-04-28

22 actions seeded (11 liftup OASIS + 11 feeder); both use `riot3` adapterKey + unified JSON format

---

## ✅ Pre-Phase 3 Bug Fixes — Done 2026-04-29

Bugs found when running Testcontainer integration tests for the first time:

| Bug | Root Cause | Fix |
|---|---|---|
| Station not persisted (500/BadRequest on order submit) | `b.Ignore(m => m.Stations)` — EF ignores Map's Stations navigation; `AddStationCommandHandler` adds to in-memory only | `MapRepository.Update`: detect detached stations, call `_dbContext.Stations.Add(s)` |
| Trip creation 500 NullRef | Tests sent `PickupStationId/DropStationId`; `DispatchTripCommand` requires `Legs` list | `BuildSingleLeg()` helper; update tests |
| VehicleType not found | Tests used `VehicleTypeId = Guid.NewGuid()` (doesn't exist); no HTTP API for VehicleType | `CreateVehicleTypeAsync()` — EF direct insert |
| List returns `[]` | `GetDeliveryOrdersQuery` defaults to `Submitted`; orders are `ReadyToPlan` after submit flow | Test query: `?status=ReadyToPlan` |
| PendingModelChangesWarning | `AddTenantId` migration captured column add but not index change | Scaffold `FixTenantIndexes` for DeliveryOrderDbContext + AuthDbContext |

---

## ⏳ Phase 3 — Integration Test Scenarios

**Objective:** automate the 8 readiness scenarios using existing harness.

**Key challenge — Test #1 (E2E Pipeline):**
The full Submit → Plan → Dispatch flow runs through MassTransit consumers. The factory uses `OutboxEventBus` (no RabbitMQ), so consumers don't fire automatically. Two options:

- **Option A** — Add in-process MassTransit bus in factory → consumers run automatically (richer test, ~5–6 days)
- **Option B** — Test each step manually via HTTP (submit → plan endpoint → dispatch endpoint → webhook) → simpler (4 days)

**Test cases:**

| # | Test Class | Scenarios |
|---|---|---|
| 1 | `EndToEndPipelineTests` | Submit order → plan → dispatch → RIOT3 complete → order Completed |
| 2 | `Riot3WebhookTests` | `finished` → task complete; `failed` → exception raised; unknown → safe log |
| 3 | `IdempotencyTests` | Duplicate POST + same key → same orderId; different key → new orderId |
| 4 | `SlaValidationTests` | SLA < 30 min → 400; valid SLA → ReadyToPlan |
| 5 | `ChargingPolicyTests` | Battery < threshold → `VehicleBatteryLowIntegrationEvent` in outbox |
| 6 | `OutboxTests` | Event write → outbox row; processor marks ProcessedAt; failure → retryable |
| 7 | `CapabilityAssignmentTests` | LIFT job → only LIFT vehicle assigned; non-LIFT → rejected |
| 8 | `AmendmentTimelineTests` | PATCH order → amendment record; GET timeline → includes amendment event |

**Acceptance Criteria:**
- All 8 scenarios automated in `tests/Integration/AMR.DeliveryPlanning.IntegrationTests/`
- Tests run via `dotnet test` with no external dependencies beyond Testcontainers
- Tests fail if EF query filters are removed (regression guard)

**Estimate:** 4–6 days

**Immediate next steps:**
1. Decide Option A vs B for E2E consumer flow
2. Write `EndToEndPipelineTests.cs`
3. Write `Riot3WebhookTests.cs`
4. Write `IdempotencyTests.cs`, `SlaValidationTests.cs`, `OutboxTests.cs`, `CapabilityAssignmentTests.cs`, `AmendmentTimelineTests.cs`
5. Target: 28+ integration tests passing (20 existing + 8 new)

---

## ⏳ Phase 4 — Load & Stress Testing

**Objective:** prove 500 orders/min NFR before launch.

**k6 scripts** in `tests/load/`:
- `setup.js` — seed map/stations/vehicle type/vehicle
- `submit_burst.js` — order submission burst
- `mixed.js` — planning + assignment + dispatch mix
- `webhook_callbacks.js` — simulate RIOT3 completions

**Capture:** p50/p95/p99 latency, error rate, DB CPU/locks, Redis hit rate, outbox backlog, RabbitMQ queue depth

**Runs:** smoke (1 min) → soak (30–60 min) → spike (2× target)

**Acceptance:** ≥ 500 orders/min, p95 < 500ms, error rate < 0.1%, stable connection pool

**Estimate:** 2–3 days

---

## ⏳ Phase 5 — Release Gate

**Checklist (owner / date / evidence required):**

- [ ] Secrets via env vars or Vault/KMS: `Jwt:Secret`, `ConnectionStrings:DefaultConnection`, `RabbitMq:*`, RIOT3 credentials
- [ ] Readiness check covers all required dependencies (add RabbitMQ health check)
- [ ] Logs, traces (Jaeger/OTLP), metrics visible in target environment
- [ ] PostgreSQL backup and restore procedure tested
- [ ] Rollback procedure documented and tested
- [ ] RIOT3 `Riot3RouteModels.cs` response shape verified against vendor spec
- [ ] Action catalog (22 actions) verified against RIOT3 vendor spec
- [ ] Deployment runbook includes `dotnet ef database update` step
- [ ] Final `PRODUCTION_READINESS.md` update: status, test evidence, load evidence, owner/date per gate

**Estimate:** 1–2 days

---

## Remaining Timeline

| Phase | Estimate |
|---|---:|
| **3 — Integration tests** | **4–6 days** |
| **4 — Load/stress testing** | **2–3 days** |
| **5 — Release gate** | **1–2 days** |
| **Total remaining** | **~1.5 weeks** |

---

## Reference

### Endpoint Summary (55 total)

| Module | จำนวน |
|---|---|
| Auth | 1 |
| DeliveryOrder | 7 |
| Planning | 12 |
| Dispatch | 17 |
| Fleet | 10 |
| Facility | 8 |
| Webhooks (RIOT3) | 3 |
| **รวม** | **55** |

### Local Dev Setup

```bash
docker compose up -d
# PostgreSQL :5434 | RabbitMQ :5672/:15672 | Redis :6379 | Jaeger :16686 | API :5219

open http://localhost:5219/swagger
curl http://localhost:5219/health        # → Healthy
curl http://localhost:5219/health/ready  # → Healthy

open http://localhost:16686   # Jaeger traces
open http://localhost:15672   # RabbitMQ (guest:guest)
```

### Nice-to-Have (Post-launch)

| งาน | เหตุผล |
|---|---|
| OR-Tools CVRP solver | Route quality ดีขึ้นสำหรับ large batches |
| WebSocket / SSE สำหรับ real-time trip progress | Operator console |
| Operator UI / Dashboard | Web-based monitoring |
| Map sync กับ RIOT3 vendor | Auto-sync station/map data |
| Multi-facility routing | Cross-facility job planning |
| POST /api/fleet/vehicle-types endpoint | ปัจจุบันสร้างผ่าน EF โดยตรงเท่านั้น |
| RabbitMQ health check | ปัจจุบัน `health/ready` ตรวจแค่ postgres + redis |
