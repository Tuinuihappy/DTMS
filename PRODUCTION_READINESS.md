# Production Readiness Checklist

> **Last Updated:** 2026-04-29
> **Build Status:** Passing | **Tests:** 54 unit + 20 integration (74 total)
> **Current State:** Staging-ready prototype — NOT production-ready
> **Fixed (2026-04-28):** C1, C2, C3, C4, C5, H1, H2, M1, M2, M4, M5
> **Fixed (2026-04-29):** H3 (Multi-tenancy), Phase 0 baseline fixes, Phase 1 EF Migrations, Pre-Phase 3 integration test bugs

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
- [TripRepository.cs](src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Repositories/TripRepository.cs) — implement ด้วย `RobotTasks` → `TripId` → load Trip
- [Riot3TaskEventConsumer.cs](src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Consumers/Riot3TaskEventConsumer.cs) — ทั้งสอง consumer ใช้ `GetTripByTaskIdAsync` แทน `GetActiveTripsByVehicleAsync(Guid.Empty)`

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
  - เพิ่ม `DbSet<VehicleGroupMember> VehicleGroupMembers`
  - ลบ value conversion สำหรับ `VehicleIds`, เพิ่ม `b.Ignore(g => g.VehicleIds)`
  - FK cascade จาก `VehicleGroupId` → `VehicleGroups` และ `VehicleId` → `Vehicles`
  - Index บน `VehicleId` สำหรับ reverse lookup
  - `xmin` PostgreSQL system column เป็น optimistic-concurrency token (auto-increment ทุก UPDATE — EF throw `DbUpdateConcurrencyException` ถ้า concurrent write)
- [VehicleGroup.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Domain/Entities/VehicleGroup.cs) — เพิ่ม `internal void LoadVehicleIds(IEnumerable<Guid>)` สำหรับ repository reconstitution (แยกออกจาก domain operation `AddVehicle`)
- [VehicleGroupRepository.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Repositories/VehicleGroupRepository.cs) — เขียนใหม่ทั้งหมด:
  - `GetByIdAsync` — load group + query members แยก
  - `GetAllAsync` — **single query สำหรับ members ทั้งหมด** (ไม่มี N+1), group ด้วย dictionary
  - `AddAsync` — insert members พร้อมกัน
  - `UpdateAsync` — delete-then-reinsert members (safe for small collections)

**Additional fixes (2026-04-28):**
- [Vehicle.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Domain/Entities/Vehicle.cs) — ลบ `_groupIds`, `GroupIds`, `AddToGroup()`, `RemoveFromGroup()` ออก — `VehicleGroupMembers` คือ single source of truth
- [FleetDbContext.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Data/FleetDbContext.cs) — ลบ `GroupIds` value conversion ออกจาก Vehicle mapping
- [VehicleRepository.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Repositories/VehicleRepository.cs) — `GetByGroupAsync` ใช้ inner join กับ `VehicleGroupMembers` แทน string `.Contains()`
- [VehicleGroupCommandHandlers.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Application/Commands/VehicleGroup/VehicleGroupCommandHandlers.cs) — `AddVehicleToGroup` / `RemoveVehicleFromGroup` save เพียงครั้งเดียวผ่าน `_groupRepo` เท่านั้น — ไม่มี split-transaction อีกต่อไป, `RemoveVehicleFromGroup` ไม่ต้องการ `IVehicleRepository` เลย

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

**Files changed / created:**

- [SimpleRouteCostCalculator.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Services/SimpleRouteCostCalculator.cs) — inject `FacilityDbContext`, query `RouteEdges` จริง; log `Warning` ชัดเจนพร้อมคำแนะนำเมื่อ edge ไม่พบ (แทน silent 999.0)
- [Map.cs](src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Map.cs) + [Station.cs](src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Station.cs) — เพิ่ม `VendorRef string?` + `SetVendorRef()` (consistent กับ `FacilityResource.VendorRef`)
- [FacilityDbContext.cs](src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure/Data/FacilityDbContext.cs) — map `VendorRef`, unique partial index บนทั้งสอง entity
- [Riot3RouteModels.cs](src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure/Services/Riot3RouteModels.cs) _(new)_ — response models สำหรับ RIOT3 route cost API
- [Riot3RouteClient.cs](src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure/Services/Riot3RouteClient.cs) _(new)_ — `IRiot3RouteClient` + implementation พร้อม error handling, ไม่ crash เมื่อ RIOT3 down
- [RouteEdgeSyncService.cs](src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure/Services/RouteEdgeSyncService.cs) _(new)_ — `BackgroundService`: sync ทุก 30 min, SemaphoreSlim 5 concurrent calls, atomic delete+insert ต่อ map ใน transaction เดียว
- [ModuleServiceRegistration.cs](src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs) + [appsettings.json](src/AMR.DeliveryPlanning.Api/appsettings.json) — register `IRiot3RouteClient`, `RouteEdgeSyncService`; config `VendorAdapter:Riot3:RouteSync:IntervalMinutes`

**วิธีเปิดใช้ sync:**
```bash
# Set VendorRef บน Map และ Station ที่ต้องการ sync ผ่าน Facility API
# PATCH /api/facility/maps/{id}     { "vendorRef": "map_floor1" }
# PATCH /api/facility/stations/{id} { "vendorRef": "ST_A1" }
# → RouteEdgeSyncService จะ sync อัตโนมัติภายใน 15s หลัง startup แล้วทุก 30 min
```

> **Note:** RIOT3 response shape ใน `Riot3RouteModels.cs` เป็น approximation — verify กับ vendor spec จริงก่อน go-live

---

### ~~C4. Distance to Pickup Hardcoded = 10.0~~ ✅ Fixed (Production-grade) 2026-04-28

**Files changed:**
- [IFleetVehicleProvider.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Domain/Services/IFleetVehicleProvider.cs) — เพิ่ม `Guid pickupStationId` parameter
- [FleetVehicleProvider.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Services/FleetVehicleProvider.cs):
  - inject `IRouteCostCalculator`, คำนวณจาก `vehicle.CurrentNodeId` จริง (fallback `999.0` ถ้า null)
  - **เปลี่ยนจาก sequential `foreach` เป็น `Task.WhenAll`** — N vehicles = N parallel Redis lookups แทน N sequential round-trips
- [GreedyVehicleSelector.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Services/GreedyVehicleSelector.cs):
  - ส่ง `pickupStationId` เข้า `GetIdleVehiclesAsync`
  - **ลบ `_cachedVehicles` static `List<VehicleCandidate>`** — thread-unsafe (`RemoveAll` + `Add` ไม่ atomic), race condition ถ้า MassTransit consumers เรียกพร้อมกัน
  - **ลบ `UpdateVehicleCache()` static method** — dead code ที่ไม่มี caller ใดเรียกเลย
  - **ลบ in-memory fallback path** — ถ้าไม่มี idle vehicle ให้ return `null` ตรงๆ แทนที่จะ fallback ไปยัง list ว่างที่ทำให้เข้าใจผิด

---

### ~~C5. SubmitDeliveryOrder Parse LocationCode เป็น Guid โดยตรง~~ ✅ Fixed (Option B) 2026-04-28

**Root cause:** fix เดิม (`Guid.TryParse` only) มี latent bug — ไม่เคยเรียก `order.MarkAsValidated()` ทำให้ `order.Status` ค้างที่ `Submitted` และ `order.PickupStationId` = `null` ตลอด

**Fix — Option B (Query Station จาก Facility DB):**

**Files created:**
- [IStationLookup.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Services/IStationLookup.cs) _(new)_ — cross-module abstraction ใน Application layer
- [FacilityStationLookup.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/Services/FacilityStationLookup.cs) _(new)_ — implementation ผ่าน `FacilityDbContext.Stations.AnyAsync`

**Files changed:**
- [DeliveryOrder.Infrastructure.csproj](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.csproj) — เพิ่ม reference ไปยัง `Facility.Infrastructure` (same pattern กับ Planning → Fleet)
- [SubmitDeliveryOrderCommandHandler.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Commands/SubmitDeliveryOrder/SubmitDeliveryOrderCommandHandler.cs):
  1. `Guid.TryParse` → `Result.Failure` ถ้า format ผิด
  2. `IStationLookup.ExistsAsync` → `Result.Failure` ถ้า station ไม่มีอยู่ใน Facility DB
  3. `order.MarkAsValidated(pickupStationId, dropStationId)` → set `PickupStationId`/`DropStationId`, Status: `Validated`
  4. `order.MarkReadyToPlan()` → Status: `ReadyToPlan` ก่อน publish event
- [ModuleServiceRegistration.cs](src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs) — register `IStationLookup → FacilityStationLookup`

**Domain status flow ที่ถูกต้อง:**
```
Submit → Submitted → MarkAsValidated → Validated → MarkReadyToPlan → ReadyToPlan → publish event
```

---

## High — Security & Data Loss

### ~~H1. JWT Secret และ Credentials Hardcoded~~ ✅ Fixed 2026-04-28

**Files changed:**
- [appsettings.json](src/AMR.DeliveryPlanning.Api/appsettings.json) — ล้าง plaintext secrets (`Jwt:Secret`, `ConnectionStrings`, `RabbitMq:Password`) เป็นค่าว่าง
- [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs) — เพิ่ม fail-fast: throw `InvalidOperationException` ถ้า `Jwt:Secret` ไม่ถูก set (ไม่มี silent fallback อีกต่อไป)
- `.gitignore` มี `appsettings.Development.json` อยู่แล้ว — ไม่ต้องแก้เพิ่ม

**Dev setup (ต้องทำครั้งเดียวต่อ machine):**
```bash
dotnet user-secrets set "Jwt:Secret" "<strong-random-key-min-32-chars>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5434;Database=amr_delivery_planning;Username=postgres;Password=<secret>"
dotnet user-secrets set "RabbitMq:Username" "guest"
dotnet user-secrets set "RabbitMq:Password" "<secret>"
```

**Production:** ใช้ environment variables หรือ Vault/KMS — อย่า commit ค่าใด ๆ ลง appsettings

---

### ~~H2. In-Memory State หายเมื่อ Pod Restart~~ ✅ Fixed 2026-04-28

| Component | เดิม | แก้เป็น |
|---|---|---|
| `InMemoryActionCatalogService` | Process memory | `vendoradapter.ActionCatalogEntries` table |
| `InMemoryCostModelService` | Process memory (Singleton) | `planning.CostModelConfigs` table (Scoped, write-through) |
| `VendorAdapterFactory._vehicleAdapterMap` | Static dict | `fleet.Vehicles.AdapterKey` column |

**Files created:**
- [VendorAdapterDbContext.cs](src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure/Data/VendorAdapterDbContext.cs) _(new)_ — schema: `vendoradapter`, maps `ActionCatalogEntries`
- [DbActionCatalogService.cs](src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure/Services/DbActionCatalogService.cs) _(new)_ — EF-backed, replaces `InMemoryActionCatalogService`
- [CostModelConfigRecord.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Data/Records/CostModelConfigRecord.cs) _(new)_ — persistence record (null VehicleTypeKey = global default)
- [DbCostModelService.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Services/DbCostModelService.cs) _(new)_ — write-through: load from DB on first request, sync on `UpdateConfig`

**Files changed:**
- [Vehicle.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Domain/Entities/Vehicle.cs) — เพิ่ม `AdapterKey string` (default `"riot3"`)
- [FleetDbContext.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Data/FleetDbContext.cs) — map `AdapterKey` column
- [RegisterVehicleCommand.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Application/Commands/RegisterVehicle/RegisterVehicleCommand.cs) + handler — เพิ่ม optional `AdapterKey` parameter (default `"riot3"`)
- [VendorAdapterFactory.cs](src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure/Services/VendorAdapterFactory.cs) — inject `FleetDbContext`, query `Vehicle.AdapterKey` แทน static dict; ลบ `_vehicleAdapterMap` + `RegisterVehicleAdapter`
- [VendorAdapterServiceRegistration.cs](src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure/VendorAdapterServiceRegistration.cs) — register `VendorAdapterDbContext`, swap `DbActionCatalogService`
- [ModuleServiceRegistration.cs](src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs) — swap `DbCostModelService` (Scoped แทน Singleton)
- [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs) — init `vendoradapter` schema + `SeedActionCatalogAsync` (upsert defaults ทุก startup)

---

### ~~H3. ไม่มี Multi-Tenancy~~ ✅ Fixed 2026-04-29

**Files created:**
- [SharedKernel/Tenancy/ITenantContext.cs](src/AMR.DeliveryPlanning.SharedKernel/Tenancy/ITenantContext.cs) — read-only scoped interface
- [SharedKernel/Tenancy/TenantContext.cs](src/AMR.DeliveryPlanning.SharedKernel/Tenancy/TenantContext.cs) — scoped implementation with `Set(Guid)`
- [Api/Middlewares/TenantContextMiddleware.cs](src/AMR.DeliveryPlanning.Api/Middlewares/TenantContextMiddleware.cs) — resolves `tenant_id` JWT claim per request
- [tests/Integration/.../TenantIsolationTests.cs](tests/Integration/AMR.DeliveryPlanning.IntegrationTests/TenantIsolationTests.cs) — 5 cross-tenant isolation tests

**Domain entities with TenantId added:**
- `DeliveryOrder`, `Job`, `Trip`, `Vehicle`, `VehicleGroup` — constructors require `tenantId`
- 8 command handlers inject `ITenantContext` and pass `TenantId` to constructors

**EF Global Query Filters:**
- `DeliveryOrderDbContext`, `PlanningDbContext`, `DispatchDbContext`, `FleetDbContext` — inject `ITenantContext`
- `HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)` on all 5 tenant-owned aggregate roots
- `OrderKey` unique index changed to `(TenantId, OrderKey)` for tenant-local uniqueness

**Events and Consumers:**
- `DeliveryOrderReadyForPlanningIntegrationEvent`, `PlanCommittedIntegrationEvent`, `TripCompletedIntegrationEvent` — added `TenantId`
- Cross-module consumers set `TenantContext` from event `TenantId` before DB access
- RIOT3 webhook consumers use `IgnoreQueryFilters()` to resolve trip, then set tenant from found trip

**Auth:**
- `AppUser.TenantId` added; JWT issues `tenant_id` claim
- Register endpoint reads `X-Tenant-Id` header; seeded admin uses `SystemTenantId`
- `AddTenantId` migration scaffolded for Auth schema

**Migrations scaffolded:** `AddTenantId` for DeliveryOrder, Planning, Dispatch, Fleet, Auth (5 modules)

---

## Medium — Operational Gaps

### ~~M1. ใช้ `EnsureCreated` แทน EF Migrations~~ ✅ Infrastructure Ready 2026-04-28

**Files changed:**
- [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs) — แทน `EnsureCreated`/`CreateSchemaAndTables` ด้วย `ApplyMigrationsAsync`:
  - ถ้ามี migrations scaffolded: ใช้ `MigrateAsync()` (production path)
  - ถ้ายังไม่มี migrations: log warning + `EnsureCreated` fallback (dev path)
- `Microsoft.EntityFrameworkCore.Design` เพิ่มใน 6 infrastructure projects
- `IDesignTimeDbContextFactory<T>` สร้างใน **ทุก** module (8 factories) — อ่าน connection string จาก env `ConnectionStrings__DefaultConnection`

**⚠️ ขั้นตอนที่ต้องทำก่อน deploy production — scaffold migrations 1 ครั้ง:**
```bash
cd d:\DTMS

# Facility
dotnet ef migrations add InitialCreate \
  --project src/Modules/Facility/AMR.DeliveryPlanning.Facility.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations

# Fleet
dotnet ef migrations add InitialCreate \
  --project src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations

# DeliveryOrder
dotnet ef migrations add InitialCreate \
  --project src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations

# Planning
dotnet ef migrations add InitialCreate \
  --project src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations

# Dispatch
dotnet ef migrations add InitialCreate \
  --project src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations

# VendorAdapter
dotnet ef migrations add InitialCreate \
  --project src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure \
  --startup-project src/AMR.DeliveryPlanning.Api \
  --output-dir Migrations

# Auth + Outbox (ใน API project เอง)
dotnet ef migrations add InitialCreate \
  --context AuthDbContext \
  --project src/AMR.DeliveryPlanning.Api \
  --output-dir Auth/Migrations

dotnet ef migrations add InitialCreate \
  --context OutboxDbContext \
  --project src/AMR.DeliveryPlanning.Api \
  --output-dir Infrastructure/Outbox/Migrations
```

> **Dev workflow ปัจจุบัน:** ยังใช้ `EnsureCreated` fallback อยู่ (log warning จะขึ้นทุก startup) — ทำงานได้ปกติ
> **Production:** ต้อง scaffold migrations ตามคำสั่งข้างต้นก่อน จากนั้น `ApplyMigrationsAsync` จะใช้ `MigrateAsync()` อัตโนมัติ

---

### ~~M2. ไม่มี Health Checks~~ ✅ Fixed 2026-04-28

**File changed:** [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs)

| Endpoint | พฤติกรรม |
|---|---|
| `GET /health` | Liveness — 200 ถ้า process alive (ไม่ต้องการ auth) |
| `GET /health/ready` | Readiness — ตรวจ postgres + redis (tag: `ready`) |

ใช้ inline lambda checks กับ `Npgsql.NpgsqlConnection` และ `StackExchange.Redis.ConnectionMultiplexer` ที่มีอยู่แล้ว โดยไม่ต้องเพิ่ม package ใหม่

> **TODO (post-launch):** เปลี่ยนเป็น `AspNetCore.HealthChecks.Npgsql` / `.RabbitMQ` / `.Redis` เมื่อ pin version ใน `Directory.Packages.props` แล้ว

---

### M3. Integration Tests เป็น Stubs

**Problem:** `*IntegrationTests` แต่ละ project มีแค่ 1 placeholder test

**Priority test scenarios:**
- [ ] End-to-end: Submit order → Plan → Dispatch → Task completion (RIOT3 simulate)
- [ ] RIOT3 webhook: `taskEventType=finished/failed` → Dispatch update
- [ ] Idempotency key: duplicate POST → return same orderId
- [ ] SLA validation: SLA ภายใน 30 นาที → rejection
- [ ] ChargingPolicy: battery below threshold → `VehicleBatteryLowIntegrationEvent`
- [ ] Outbox processor: message written → published via MassTransit
- [ ] Capability assignment: job requires "LIFT" → only liftup vehicles selected
- [ ] Amendment: PATCH order → amendment record created

---

### ~~M4. ไม่มี Rate Limiting~~ ✅ Fixed 2026-04-28

**File changed:** [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs)
— Global fixed-window limiter: **100 req/min** partition by client IP
— ใช้ `GlobalLimiter` (แทน named policy) เพื่อครอบคลุมทุก endpoint อัตโนมัติ
— เกินลิมิต → HTTP `429 Too Many Requests`

---

### ~~M5. Feeder Adapter Endpoints ยังสมมติ~~ ✅ Fixed 2026-04-28

**Root cause ที่แก้:** feeder และ liftup เป็น **vendor เดียวกัน** — ทั้งคู่ใช้ RIOT3 API + format เดียวกัน (`actionType / 0 / 1`)

**Files changed:**
- [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs) — `SeedActionCatalogAsync` อัปเดตครบตาม vendor spec:
  - liftup (OASIS): 11 actions (LIFT, DROP, LIFT_FRONT_REAR, LIFT_PLATFORM_SAFE, LIFT_PLATFORM, ROTATE_PLATFORM, SYNC_ROTATE_ON/OFF, PLATFORM_INIT, SHELF_CORRECT, LIFT_CALIBRATE)
  - feeder: 11 actions (INIT, LIFT, DROP, RIGHT_LOAD/UNLOAD, FRONT_PROBE, FRONT_AXIS_HOME, LEFT/RIGHT_STOP_HOME/BLOCK)
  - **adapterKey เปลี่ยนจาก `"feeder"` เป็น `"riot3"`** สำหรับทุก feeder actions
  - JSON format unified: `{"actionType":"<ID>","0":"<P0>","1":"<P1>"}` ทั้งสองประเภท
- [VendorAdapterFactory.cs](src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure/Services/VendorAdapterFactory.cs) — `"feeder"` adapterKey resolve เป็น Riot3CommandService (pattern match รวมกับ `"riot3"`)

> **Note:** `FeederCommandService` ยังคงอยู่ใน codebase สำหรับ vendor ที่ใช้ protocol ต่างหากในอนาคต

---

## Nice-to-Have (Post-launch)

| งาน | เหตุผล |
|---|---|
| OR-Tools CVRP solver (แทน Nearest Neighbor heuristic) | Route quality ดีขึ้นสำหรับ large batches |
| WebSocket / SSE สำหรับ real-time trip progress | Operator console ต้องการ live push |
| Operator UI / Dashboard | Web-based monitoring สำหรับ trips, exceptions, KPI |
| Map synchronization กับ RIOT3 vendor | Auto-sync station/map data จาก AMR vendor |
| EF Migrations per-module setup | Clean schema evolution |
| Multi-facility routing | Cross-facility job planning |

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
| ~~Operational (partial)~~ | ~~M1 · M2 · M4 · M5~~ | ~~1 วัน~~ | ✅ Done 2026-04-28 |
| Operational | **M3 ยังค้าง** (Integration test cases) | 5–7 วัน | Pending |
| Integration Testing | test scenarios ครบ | 5–7 วัน | Pending |
| Load / Stress Testing | verify NFR (500 orders/min) | 2–3 วัน | Pending |
| Release Gate | secrets, runbook, vendor contract | 1–2 วัน | Pending |
| **รวม (เหลือ)** | **M3 + Integration Tests + Load + Release Gate** | **~2 สัปดาห์** | |
