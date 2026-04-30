# Production Readiness & Implementation Status

> **Last Updated:** 2026-04-30
> **Build:** Passing | **Tests:** 54 unit + 20 integration + 11 RIOT3 real = **85 total (all passing)**
> **State:** Staging-ready prototype — NOT production-ready
> **Remaining:** Phase 3 (integration tests) → Phase 4 (load) → Phase 5 (release gate)

---

## Production Definition of Done

| Criteria | Status |
|---|---|
| Production starts with EF migrations, not `EnsureCreated` | ✅ Done — all 8 contexts use `MigrateAsync()`; Production guard throws if no migrations |
| Tenant-owned data isolated at API, EF query filter, event, consumer | ✅ Done — query filters on 5 aggregate roots; events carry TenantId |
| Cross-tenant reads/writes covered by automated tests | ✅ Done — 5 cross-tenant isolation tests pass |
| Readiness integration scenarios run in CI without manual DB setup | ✅ Done — 85 tests pass (8 readiness scenarios + 11 RIOT3 real); Phase 3 complete 2026-04-30 |
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
| **Phase 3** | Integration test scenarios (8 readiness cases) + RIOT3 real tests (11/11) | **✅ Done 2026-04-30** |
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

**✅ Phase 3 Complete (2026-04-30):**
- [x] E2E full pipeline: Submit → plan → dispatch → complete → outbox event
- [x] RIOT3 webhook: `finished`/`failed`/unknown taskId/invalid upperKey
- [x] Idempotency key: duplicate POST → same orderId
- [x] SLA validation: < 30 min → 400 BadRequest
- [x] ChargingPolicy: battery < threshold → `VehicleBatteryLowIntegrationEvent`
- [x] Outbox processor: write → ProcessedAt marked; failure → retryable
- [x] Capability assignment: LIFT job → only LIFT vehicle assigned
- [x] Amendment + timeline: PATCH → amendment record → timeline event

**✅ RIOT3 Real Integration (Riot3RealIntegrationTests.cs — 11/11 passing 2026-04-30):**
- [x] `Direct_Health_ServerIsReachableAndRunning` — RIOT3 up at 10.204.212.28:12000
- [x] `Direct_ApiKey_IsAcceptedByRiot3` — Token accepted (Delta6FAN1)
- [x] `Direct_GetRobotState_UnknownId_ReturnsResourceNotFound` — E320003 correct
- [x] `Direct_PostOrder_OurFormat_AcceptedOrBusinessRuleRejected` — Payload format OK
- [x] `Direct_CancelOrder_AuthSucceeds_BusinessErrorOrSuccess` — Auth OK
- [x] `ThroughApp_DispatchTrip_Riot3AdapterCalled_TripInProgress`
- [x] `ThroughApp_DispatchWithRiot3_OutboxHasVehicleStateEvent`
- [x] `Webhook_Riot3TaskFinished_RealPayloadFormat_ProcessedCorrectly`
- [x] `Webhook_Riot3TaskFailed_RealPayloadFormat_PublishesFailedEvent`
- [x] `Webhook_Riot3VehicleEmergency_EmergencyFlagHandledSafely`
- [x] `Webhook_Riot3TaskStarted_NoEventPublished_Returns200`

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

**Objective:** automate the 8 readiness scenarios + verify real RIOT3 connectivity.

**Decision:** Option B (HTTP-driven steps, no in-process MassTransit bus) — all 8 test classes written.

**Status: In Progress — 2026-04-30**

**Test cases:**

| # | Test Class | Scenarios | Status |
|---|---|---|---|
| 1 | `EndToEndPipelineTests` | Submit → plan → dispatch → complete → outbox event | ✅ Written |
| 2 | `Riot3WebhookTests` | `finished`/`failed`/unknown taskId/invalid upperKey | ✅ Written |
| 3 | `IdempotencyTests` | Duplicate POST + same key → same orderId | ✅ Written |
| 4 | `SlaValidationTests` | SLA < 30 min → 400; valid → ReadyToPlan | ✅ Written |
| 5 | `ChargingPolicyTests` | Battery < threshold → `VehicleBatteryLowIntegrationEvent` | ✅ Written |
| 6 | `OutboxTests` | Event write/ProcessedAt/retry | ✅ Written |
| 7 | `CapabilityAssignmentTests` | LIFT job → only LIFT vehicle | ✅ Written |
| 8 | `AmendmentTimelineTests` | PATCH → amendment record → timeline | ✅ Written |
| 9 | `Riot3RealIntegrationTests` | Real RIOT3 server: health + auth + dispatch + webhook round-trip | ✅ 11/11 passing |

**Real RIOT3 test run 2026-04-30 — 11/11 passed ✅**

| Test | Result | Note |
|---|---|---|
| `Direct_Health_ServerIsReachableAndRunning` | ✅ Pass | Server up at 10.204.212.28:12000 |
| `Direct_ApiKey_IsAcceptedByRiot3` | ✅ Pass | Token accepted (Delta6FAN1) |
| `Direct_GetRobotState_UnknownId_ReturnsResourceNotFound` | ✅ Pass | E320003 as expected |
| `Direct_PostOrder_OurFormat_AcceptedOrBusinessRuleRejected` | ✅ Pass | Payload format accepted |
| `Direct_CancelOrder_AuthSucceeds_BusinessErrorOrSuccess` | ✅ Pass | Auth ok, business error expected |
| `ThroughApp_DispatchTrip_Riot3AdapterCalled_TripInProgress` | ✅ Pass | Trip InProgress after dispatch |
| `ThroughApp_DispatchWithRiot3_OutboxHasVehicleStateEvent` | ✅ Pass | Webhook outbox event correct |
| `Webhook_Riot3TaskFinished_RealPayloadFormat_ProcessedCorrectly` | ✅ Pass | |
| `Webhook_Riot3TaskFailed_RealPayloadFormat_PublishesFailedEvent` | ✅ Pass | errorCode preserved |
| `Webhook_Riot3VehicleEmergency_EmergencyFlagHandledSafely` | ✅ Pass | Emergency → state event |
| `Webhook_Riot3TaskStarted_NoEventPublished_Returns200` | ✅ Pass | Safe log, no outbox event |

**Run commands:**
```bash
# CI-safe (no RIOT3 network needed):
dotnet test --filter "Category!=Riot3Real"

# Real RIOT3 (requires access to 10.204.212.28:12000):
dotnet test --filter "Category=Riot3Real"
```

**Acceptance Criteria:**
- All 8 readiness scenarios automated ✅
- Tests run via `dotnet test` with no external deps beyond Testcontainers ✅
- Real RIOT3 direct auth verified — 11/11 passing 2026-04-30 ✅

---

## ⚙️ RIOT3 Production Setup — ต้องทำก่อนใช้งานจริง

Auth และ connectivity พร้อมแล้ว (11/11 tests pass) แต่ต้องทำ 3 ขั้นตอนนี้ก่อน end-to-end จะทำงานได้จริงกับ robot จริง

### 1. ลงทะเบียน Vehicle ให้ตรงกับ RIOT3 Robot

VehicleId ใน app ต้องตรงกับ `deviceKey` ของ robot จริงใน RIOT3:

```bash
# ดู robot list จาก RIOT3 ก่อน
curl -H "Authorization: app <token>" http://10.204.212.28:12000/api/v4/robots

# ลงทะเบียนใน app โดยใช้ VehicleName ที่ระบุตัวตนได้
POST /api/fleet/vehicles
{
  "VehicleName": "AMR-01",
  "VehicleTypeId": "<vehicle-type-id>",
  "AdapterKey": "riot3"
}
# → ได้ vehicleId (Guid) กลับมา ต้องผูกกับ deviceKey ใน RIOT3
```

> **ข้อจำกัดปัจจุบัน:** app ส่ง `appointVehicleKey = vehicleId.ToString()` ไปใน RIOT3 order
> แต่ RIOT3 ใช้ `deviceKey` (ซึ่งอาจเป็น string อื่น เช่น "SEER-001")
> ถ้า key ไม่ตรง RIOT3 จะ return E320003 (vehicle not found) แทนที่จะรัน task
> → ต้องตกลงกับทีม RIOT3 ว่าจะใช้ Guid ของ app หรือ map deviceKey ที่ฝั่ง vehicle registration

### 2. ตั้ง Webhook Callback URL ใน RIOT3 Admin

RIOT3 ต้อง POST กลับมาที่ app เมื่อ task เสร็จ/ล้มเหลว:

```
http://<app-host>:<port>/api/webhooks/riot3/notify
```

ขั้นตอน:
1. Login http://10.204.212.28:12000 as admin
2. Settings → Notification / Webhook → Add callback URL
3. ตั้งเป็น URL ที่ RIOT3 server เข้าถึงได้ (ไม่ใช่ localhost)

> **ถ้ารัน local ระหว่าง dev:** ใช้ [ngrok](https://ngrok.com/) expose port 5219
> ```bash
> ngrok http 5219
> # ใช้ URL ที่ได้ เช่น https://abc123.ngrok.io/api/webhooks/riot3/notify
> ```

### 3. ยืนยัน Network Path

| ทิศทาง | ต้องการ | สถานะ |
|---|---|---|
| App → RIOT3 (10.204.212.28:12000) | ส่ง order/cancel | ✅ ยืนยันแล้ว |
| RIOT3 → App (webhook) | รับ task complete/failed | ⚠️ ต้องตรวจสอบว่า RIOT3 เข้าถึง app ได้ |

```bash
# ทดสอบจาก RIOT3 server ว่าเรียก app webhook ได้ไหม
curl -X POST http://<app-host>:<port>/api/webhooks/riot3/notify \
     -H "Content-Type: application/json" \
     -d '{"type":"task","taskEventType":"finished","upperKey":"test-id"}'
# → ต้องได้ 200 OK
```

### Checklist ก่อน Go-Live กับ RIOT3

- [ ] ได้รายการ `deviceKey` ของ robot ทุกตัวจากทีม RIOT3
- [ ] ตัดสินใจ vehicle ID mapping strategy (Guid vs deviceKey)
- [ ] ลงทะเบียน vehicle ทุกตัวใน app ด้วย `AdapterKey = "riot3"`
- [ ] ตั้ง webhook callback URL ใน RIOT3 admin
- [ ] ยืนยัน RIOT3 server POST มาที่ app webhook ได้ (network path open)
- [ ] ทดสอบ end-to-end: dispatch trip จาก app → robot เคลื่อนที่ → webhook กลับ → trip Completed

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
| ~~**3 — Integration tests**~~ | ~~4–6 days~~ → **✅ Done 2026-04-30** |
| **4 — Load/stress testing** | **2–3 days** |
| **5 — Release gate** | **1–2 days** |
| **Total remaining** | **~1 week** |

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
