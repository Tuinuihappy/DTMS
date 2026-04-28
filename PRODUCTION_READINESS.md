# Production Readiness Checklist

> **Last Updated:** 2026-04-29
> **Build Status:** Passing | **Tests:** 54 unit + 20 integration (74 total)
> **Current State:** Staging-ready prototype — NOT production-ready
> **Fixed (2026-04-28):** C1, C2, C3, C4, C5, H1, H2, M1, M2, M4, M5
> **Fixed (2026-04-29):** H3 (Multi-tenancy), Phase 0–2 hardening, Pre-Phase 3 integration test bugs

---

## สถานะ Sprint ทั้งหมด

| Sprint | ขอบเขต | สถานะ |
|---|---|---|
| P1 — Critical Fixes | RIOT3 endpoint, PlanCommittedConsumer, OrderStatus, VendorAdapter wiring | Done |
| P2 — Infrastructure | Outbox, Redis cache, OpenTelemetry, CI/CD | Done |
| P3 — Dispatch Ops | Pause/Resume/Cancel, Exceptions, Reassign, Proof of Delivery | Done |
| P4 — Fleet | ChargingPolicy, Maintenance, VehicleGroup, KPI | Done |
| P5 — Facility | Route cost proxy, Stations query, Topology overlays, Resource commands | Done |
| P6 — DeliveryOrder | Amendment history, Idempotency key, SLA validation, Bulk import, Timeline | Done |
| P7 — Multi-vendor | Feeder adapter, Action catalog, RIOT3 event normalization, Capability assignment | Done |
| P8 — Advanced | Battery-aware dispatch, Predictive replanning, Explainability, Cost model | Done |
| **Hardening Phase 0** | Baseline fixes (build errors, EnsureCreated, health endpoints) | **Done 2026-04-29** |
| **Hardening Phase 1** | EF Migrations (8 DbContexts), MigrateAsync path, production guard | **Done 2026-04-29** |
| **Hardening Phase 2** | Multi-tenancy H3: ITenantContext, query filters, events, consumers, AppUser JWT | **Done 2026-04-29** |
| **Pre-Phase 3 Bug Fixes** | Station save (MapRepository), Trip Legs, VehicleType seed, migration gaps | **Done 2026-04-29** |

---

## Critical — แก้ก่อน Production (system ทำงานผิดพลาดถ้าไม่แก้)

### ~~C1. RIOT3 Task Consumer หา Trip ไม่เจอ~~ ✅ Fixed 2026-04-28

**Files changed:**
- [ITripRepository.cs](src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Repositories/ITripRepository.cs) — เพิ่ม `GetTripByTaskIdAsync(Guid taskId)`
- [TripRepository.cs](src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Repositories/TripRepository.cs) — implement ด้วย `RobotTasks` → `TripId` → load Trip; ใช้ `IgnoreQueryFilters()` เพราะ RIOT3 webhook ไม่มี tenant claim (Phase 2)
- [Riot3TaskEventConsumer.cs](src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Consumers/Riot3TaskEventConsumer.cs) — ทั้งสอง consumer ใช้ `GetTripByTaskIdAsync` แทน `GetActiveTripsByVehicleAsync(Guid.Empty)`; set `TenantContext` จาก trip ที่พบ (Phase 2)

---

### ~~C2. VehicleGroupRepository — Comma-Separated VehicleIds~~ ✅ Fixed (Production-grade) 2026-04-28

**Root cause:** `VehicleIds` เก็บเป็น comma-separated VARCHAR ทำให้เกิดปัญหา 4 อย่างในเวลาเดียวกัน:

| ปัญหา | ผลกระทบ |
|---|---|
| ไม่มี FK constraint | Vehicle ถูกลบแต่ ID ยังติดอยู่ใน group ตลอดไป |
| Race condition | 2 request save พร้อมกัน — ฝ่ายหลัง overwrite ฝ่ายแรก (lost update) |
| ไม่มี index บน VehicleId | `GetByGroupAsync` ต้อง full scan + string parse |
| ข้อมูลซ้ำ 2 ที่ | `VehicleGroup.VehicleIds` กับ `Vehicle.GroupIds` sync กันไม่ atomic |

**Fix — Normalize เป็น join table `fleet.VehicleGroupMembers`:**

**Files changed:**
- [VehicleGroupMember.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Data/VehicleGroupMember.cs) _(new)_ — infrastructure-only entity, composite PK `(VehicleGroupId, VehicleId)`
- [FleetDbContext.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Data/FleetDbContext.cs):
  - เพิ่ม `DbSet<VehicleGroupMember> VehicleGroupMembers`; FK cascade; Index บน `VehicleId`
  - `xmin` PostgreSQL system column เป็น optimistic-concurrency token
- [VehicleGroup.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Domain/Entities/VehicleGroup.cs) — เพิ่ม `internal void LoadVehicleIds(IEnumerable<Guid>)` + `TenantId` (Phase 2)
- [VehicleGroupRepository.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Repositories/VehicleGroupRepository.cs) — เขียนใหม่ทั้งหมด: single query ไม่มี N+1

---

### ~~C3. Route Cost เป็น Pseudo-distance (Hash-based)~~ ✅ Fixed (Hybrid) 2026-04-28

**Architecture — Hybrid (DB primary + RIOT3 background sync):**

```
Planning request
      │
      ▼
CachedRouteCostCalculator  (Redis TTL 15s)
      │ cache miss
      ▼
SimpleRouteCostCalculator  ──── facility.RouteEdges (DB)
                                        ▲
                                        │ atomic replace every 30 min
                                RouteEdgeSyncService  (BackgroundService)
                                        │ max 5 concurrent calls
                                        ▼
                                RIOT3  /api/v4/route/costs/{mapVendorRef}/{stationVendorRef}
```

> **Note:** RIOT3 response shape ใน `Riot3RouteModels.cs` เป็น approximation — verify กับ vendor spec จริงก่อน go-live

---

### ~~C4. Distance to Pickup Hardcoded = 10.0~~ ✅ Fixed (Production-grade) 2026-04-28

- `FleetVehicleProvider`: inject `IRouteCostCalculator`, คำนวณจาก `vehicle.CurrentNodeId` จริง (fallback `999.0`)
- `GreedyVehicleSelector`: ลบ `_cachedVehicles` static list (thread-unsafe), ลบ in-memory fallback

---

### ~~C5. SubmitDeliveryOrder Parse LocationCode เป็น Guid โดยตรง~~ ✅ Fixed (Option B) 2026-04-28

**Domain status flow ที่ถูกต้อง:**
```
Submit → Submitted → MarkAsValidated → Validated → MarkReadyToPlan → ReadyToPlan → publish event
```

- `IStationLookup.ExistsAsync` ตรวจ station ใน FacilityDB ก่อน validate
- `order.MarkAsValidated(pickupId, dropId)` + `order.MarkReadyToPlan()` เรียกแน่นอน

---

## High — Security & Data Loss

### ~~H1. JWT Secret และ Credentials Hardcoded~~ ✅ Fixed 2026-04-28

- `appsettings.json` ล้าง plaintext secrets
- `Program.cs` fail-fast: throw ถ้า `Jwt:Secret` ไม่ถูก set

**Dev setup:**
```bash
dotnet user-secrets set "Jwt:Secret" "<strong-random-key-min-32-chars>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5434;..."
dotnet user-secrets set "RabbitMq:Username" "guest"
dotnet user-secrets set "RabbitMq:Password" "<secret>"
```

**docker-compose dev:** `Jwt__Secret=dev-only-secret-min-32-chars-placeholder!` (ไม่ใช่ production)

---

### ~~H2. In-Memory State หายเมื่อ Pod Restart~~ ✅ Fixed 2026-04-28

| Component | เดิม | แก้เป็น |
|---|---|---|
| `InMemoryActionCatalogService` | Process memory | `vendoradapter.ActionCatalogEntries` table |
| `InMemoryCostModelService` | Process memory (Singleton) | `planning.CostModelConfigs` table (Scoped, write-through) |
| `VendorAdapterFactory._vehicleAdapterMap` | Static dict | `fleet.Vehicles.AdapterKey` column |

---

### ~~H3. ไม่มี Multi-Tenancy~~ ✅ Fixed 2026-04-29

**Files created:**
- [SharedKernel/Tenancy/ITenantContext.cs](src/AMR.DeliveryPlanning.SharedKernel/Tenancy/ITenantContext.cs) — read-only scoped interface
- [SharedKernel/Tenancy/TenantContext.cs](src/AMR.DeliveryPlanning.SharedKernel/Tenancy/TenantContext.cs) — scoped implementation with `Set(Guid)`
- [Api/Middlewares/TenantContextMiddleware.cs](src/AMR.DeliveryPlanning.Api/Middlewares/TenantContextMiddleware.cs) — resolves `tenant_id` JWT claim per request
- [tests/Integration/.../TenantIsolationTests.cs](tests/Integration/AMR.DeliveryPlanning.IntegrationTests/TenantIsolationTests.cs) — 5 cross-tenant isolation tests (all pass)

**Domain entities with TenantId added:**
- `DeliveryOrder`, `Job`, `Trip`, `Vehicle`, `VehicleGroup` — constructors require `tenantId`
- 8 command handlers inject `ITenantContext`

**EF Global Query Filters:**
- `DeliveryOrderDbContext`, `PlanningDbContext`, `DispatchDbContext`, `FleetDbContext`
- `HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)` on 5 aggregate roots
- `OrderKey` unique index → `(TenantId, OrderKey)`; `Username` unique index → `(TenantId, Username)`

**Events and Consumers:**
- `DeliveryOrderReadyForPlanningIntegrationEvent`, `PlanCommittedIntegrationEvent`, `TripCompletedIntegrationEvent` — TenantId added
- RIOT3 consumers: `IgnoreQueryFilters()` → set tenant from found trip

**Auth:**
- `AppUser.TenantId`; `SystemTenantId` constant for seeded admin
- JWT issues `tenant_id` claim; register endpoint reads `X-Tenant-Id` header

**Decisions:**
- Facility (Maps, Stations, RouteEdges) — **shared/global** (shared physical infrastructure)
- VendorAdapter (ActionCatalog) — **global** (same vendor protocol for all tenants)

---

## Medium — Operational Gaps

### ~~M1. ใช้ `EnsureCreated` แทน EF Migrations~~ ✅ Fully Done 2026-04-29

**Migrations scaffolded (all 8 DbContexts):**

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

**Production guard:** `ASPNETCORE_ENVIRONMENT=Production` + no migrations → `InvalidOperationException` at startup (no silent fallback)

**Dev fallback:** DB pre-created by `POSTGRES_DB` env var → `CreateTablesAsync()` ensures all schemas created

**Verified:** all 8 contexts log `"Applying N pending migration(s)"` on fresh DB; 32 tables created

---

### ~~M2. ไม่มี Health Checks~~ ✅ Fixed 2026-04-28

| Endpoint | พฤติกรรม |
|---|---|
| `GET /health` | Liveness — 200 ถ้า process alive (anonymous) |
| `GET /health/ready` | Readiness — ตรวจ postgres + redis (tag: `ready`) |

---

### M3. Integration Tests — Infrastructure Complete, Scenarios Pending

**สถานะปัจจุบัน:**
- ✅ Testcontainer harness พร้อม (`DtmsWebApplicationFactory`, PostgreSQL Testcontainers, in-memory Redis)
- ✅ Auth helper รองรับ multi-tenant JWT (`GetClientForTenantAsync`)
- ✅ 20 integration tests ผ่าน (basic E2E flows + cross-tenant isolation)
- ✅ Bug fixes: station persistence (MapRepository), Trip Legs format, VehicleType seeding

**Scenarios ที่ยังค้าง (Phase 3 work):**
- [ ] E2E full pipeline: Submit order → Auto-plan → Auto-dispatch → RIOT3 task complete
- [ ] RIOT3 webhook: `finished`/`failed` → Dispatch state update
- [ ] Idempotency key: duplicate POST returns same orderId
- [ ] SLA validation: SLA < 30 min → rejection
- [ ] ChargingPolicy: battery below threshold → `VehicleBatteryLowIntegrationEvent`
- [ ] Outbox processor: message written → marked processed → retryable on failure
- [ ] Capability assignment: job requires "LIFT" → only matching vehicles selected
- [ ] Amendment: PATCH order → amendment record + timeline event

---

### ~~M4. ไม่มี Rate Limiting~~ ✅ Fixed 2026-04-28

Global fixed-window: **100 req/min** per client IP → HTTP `429 Too Many Requests`

---

### ~~M5. Feeder Adapter Endpoints ยังสมมติ~~ ✅ Fixed 2026-04-28

Feeder + liftup unified under `riot3` adapterKey; 22 actions seeded (11 liftup OASIS + 11 feeder)

---

## Nice-to-Have (Post-launch)

| งาน | เหตุผล |
|---|---|
| OR-Tools CVRP solver (แทน Nearest Neighbor heuristic) | Route quality ดีขึ้นสำหรับ large batches |
| WebSocket / SSE สำหรับ real-time trip progress | Operator console ต้องการ live push |
| Operator UI / Dashboard | Web-based monitoring สำหรับ trips, exceptions, KPI |
| Map synchronization กับ RIOT3 vendor | Auto-sync station/map data จาก AMR vendor |
| Multi-facility routing | Cross-facility job planning |
| POST /api/fleet/vehicle-types endpoint | ปัจจุบัน VehicleType ต้องสร้างผ่าน EF โดยตรง (no HTTP API) |
| RabbitMQ health check | ปัจจุบัน `health/ready` ตรวจแค่ postgres + redis |

---

## Endpoint Summary (55 total)

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

---

## Local Dev / Staging Setup

```bash
# Start all services
docker compose up -d
# PostgreSQL :5434 | RabbitMQ :5672/:15672 | Redis :6379 | Jaeger :16686 | API :5219

# Swagger UI
open http://localhost:5219/swagger

# Health checks
curl http://localhost:5219/health        # → Healthy
curl http://localhost:5219/health/ready  # → Healthy (postgres + redis)

# Jaeger (distributed traces)
open http://localhost:16686

# RabbitMQ Management
open http://localhost:15672   # guest:guest
```

---

## ประมาณเวลาก่อน Production-ready

| Phase | งาน | ประมาณ | สถานะ |
|---|---|---|---|
| ~~Critical Fixes~~ | ~~C1–C5~~ | ~~3–5 วัน~~ | ✅ Done 2026-04-28 |
| ~~Security Hardening~~ | ~~H1 · H2 · H3~~ | ~~3–5 วัน~~ | ✅ Done 2026-04-29 |
| ~~Operational (M1–M2, M4–M5)~~ | ~~Migrations, Health, Rate limit, Feeder~~ | ~~1 วัน~~ | ✅ Done 2026-04-28/29 |
| ~~Baseline + Migration + Multi-tenancy~~ | ~~Phase 0, 1, 2~~ | ~~1–2 สัปดาห์~~ | ✅ Done 2026-04-29 |
| **Integration Testing (M3)** | Readiness scenarios (8 test cases) | **5–7 วัน** | **Pending — Phase 3** |
| **Load / Stress Testing** | verify NFR (500 orders/min) via k6 | **2–3 วัน** | **Pending — Phase 4** |
| **Release Gate** | secrets, observability, backup/restore, runbook, vendor contract | **1–2 วัน** | **Pending — Phase 5** |
| **รวม (เหลือ)** | **Integration Tests + Load + Release Gate** | **~1.5 สัปดาห์** | |
