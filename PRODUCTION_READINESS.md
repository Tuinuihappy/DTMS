# Production Readiness Checklist

> **Last Updated:** 2026-04-28
> **Build Status:** Passing | **Tests:** 44/44
> **Current State:** Staging-ready prototype — NOT production-ready

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

---

## Critical — แก้ก่อน Production (system ทำงานผิดพลาดถ้าไม่แก้)

### C1. RIOT3 Task Consumer หา Trip ไม่เจอ

**File:** [Riot3TaskEventConsumer.cs](src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Consumers/Riot3TaskEventConsumer.cs) (line 44, 83)

**Problem:** `GetActiveTripsByVehicleAsync(Guid.Empty)` ไม่ return trip ที่ถูกต้อง

**Fix:**
1. เพิ่ม `GetTripByTaskIdAsync(Guid taskId)` ใน `ITripRepository`
2. Implement ใน `TripRepository` — join `RobotTasks` → `Trips`
3. อัปเดต consumer ให้ใช้ method ใหม่

```csharp
// ในแต่ละ consumer ใช้:
var trip = await _tripRepo.GetTripByTaskIdAsync(taskId, ct);
```

---

### C2. VehicleGroupRepository ใช้ `.Include()` กับ Value-Converted Column

**File:** [VehicleGroupRepository.cs](src/Modules/Fleet/AMR.DeliveryPlanning.Fleet.Infrastructure/Repositories/VehicleGroupRepository.cs)

**Problem:** `VehicleIds` เป็น comma-separated value conversion ไม่ใช่ navigation property — `.Include(g => g.VehicleIds)` throw runtime error

**Fix:**
```csharp
public Task<VehicleGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
    => _db.VehicleGroups.FirstOrDefaultAsync(g => g.Id == id, ct); // ลบ .Include()
```

---

### C3. Route Cost เป็น Pseudo-distance (Hash-based)

**File:** [SimpleRouteCostCalculator.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Services/SimpleRouteCostCalculator.cs)

**Problem:** ใช้ `Math.Abs(fromId.GetHashCode() - toId.GetHashCode()) % 100` — TSP และ vehicle assignment ผิดพลาด

**Fix — Option A (ง่ายกว่า):** Query `RouteEdge.Cost` จาก FacilityDbContext
```csharp
var edge = await _facilityDb.RouteEdges.FirstOrDefaultAsync(e =>
    (e.SourceStationId == from && e.TargetStationId == to) ||
    (e.IsBidirectional && e.SourceStationId == to && e.TargetStationId == from));
return edge?.Cost ?? 999.0;
```

**Fix — Option B (ครบถ้วน):** Call RIOT3 `/api/v4/route/costs/{mapId}/{stationId}?deviceKey=` + cache Redis TTL 15s

---

### C4. Distance to Pickup Hardcoded = 10.0

**File:** [FleetVehicleProvider.cs](src/Modules/Planning/AMR.DeliveryPlanning.Planning.Infrastructure/Services/FleetVehicleProvider.cs) (line 31)

**Problem:** ทุก vehicle มี distance = 10.0 — selector เลือก vehicle โดยไม่คำนึงถึงระยะทางจริง

**Fix:**
```csharp
var distance = vehicle.CurrentNodeId.HasValue
    ? await _routeCostCalc.CalculateCostAsync(vehicle.CurrentNodeId.Value, pickupStationId, ct)
    : 999.0;
return new VehicleCandidate(v.Id, distance, v.BatteryLevel, v.VehicleTypeId, vt?.Capabilities);
```

---

### C5. SubmitDeliveryOrder Parse LocationCode เป็น Guid โดยตรง

**File:** [SubmitDeliveryOrderCommandHandler.cs](src/Modules/DeliveryOrder/AMR.DeliveryPlanning.DeliveryOrder.Application/Commands/SubmitDeliveryOrder/SubmitDeliveryOrderCommandHandler.cs)

**Problem:** `Guid.Parse(request.PickupLocationCode)` throw `FormatException` ถ้า upstream ส่ง string code เช่น `"STATION-A1"`

**Fix (เลือกหนึ่ง):**
- เปลี่ยน `PickupLocationCode` เป็น `Guid PickupStationId` ใน command โดยตรง
- หรือ Query Station จาก Facility DB ตาม location code ก่อน

---

## High — Security & Data Loss

### H1. JWT Secret และ Credentials Hardcoded

**Files:**
- [appsettings.Development.json](src/AMR.DeliveryPlanning.Api/appsettings.Development.json) — `"super-secret-key-for-development-minimum-32-chars!"`
- DB password: `postgres:postgres`, RabbitMQ: `guest:guest`

**Fix:**
```bash
# Development — ใช้ dotnet user-secrets
dotnet user-secrets set "JwtSettings:Secret" "<strong-key>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Password=<secret>"

# Production — ใช้ environment variables หรือ Vault/KMS
```

ลบ secrets ออกจาก appsettings ทุกไฟล์ และเพิ่มใน `.gitignore`

---

### H2. In-Memory State หายเมื่อ Pod Restart

**Problem:** State ที่เก็บใน memory หายทุกครั้งที่ restart

| Component | ที่เก็บ | ผลกระทบ |
|---|---|---|
| `InMemoryActionCatalogService` | Process memory | Action catalog entries หาย |
| `InMemoryCostModelService` | Process memory | Cost model ที่ config ผ่าน API หาย |
| `VendorAdapterFactory._vehicleAdapterMap` | Static dict | Vehicle→adapter mapping หาย |

**Fix:**
- `ActionCatalogEntry` → เพิ่ม DB table `vendoradapter.ActionCatalogEntries`
- `CostModelConfig` → เก็บใน `planning.CostModelConfigs` หรือ Redis
- `_vehicleAdapterMap` → เพิ่ม column `AdapterKey` ใน `fleet.Vehicles`

---

### H3. ไม่มี Multi-Tenancy

**Problem:** ไม่มี `tenantId` ในทุก entity และ query — ข้อมูล tenant ปนกัน

**Fix:**
1. เพิ่ม `TenantId` column ใน tables หลัก (DeliveryOrders, Jobs, Trips, Vehicles)
2. เพิ่ม Global Query Filter ใน EF Core:
```csharp
modelBuilder.Entity<Job>().HasQueryFilter(j => j.TenantId == _tenantContext.TenantId);
```
3. Inject `ITenantContext` จาก JWT claims

---

## Medium — Operational Gaps

### M1. ใช้ `EnsureCreated` แทน EF Migrations

**Problem:** Schema change ต้อง drop+recreate DB — ข้อมูล production หาย

**Fix:**
```bash
# สร้าง migrations ต่อ module
dotnet ef migrations add InitialCreate --project AMR.DeliveryPlanning.Planning.Infrastructure
```
อัปเดต [Program.cs](src/AMR.DeliveryPlanning.Api/Program.cs) ให้ใช้ `db.Database.MigrateAsync()` แทน `EnsureCreated()`

---

### M2. ไม่มี Health Checks

**Fix:**
```csharp
builder.Services.AddHealthChecks()
    .AddNpgsql(connectionString, name: "postgres")
    .AddRabbitMQ(name: "rabbitmq")
    .AddRedis(redisConnection, name: "redis");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
```

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

### M4. ไม่มี Rate Limiting

**Fix:**
```csharp
builder.Services.AddRateLimiter(o => o
    .AddFixedWindowLimiter("api", opt => {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    }));
app.UseRateLimiter();
```

---

### M5. Feeder Adapter Endpoints ยังสมมติ

**File:** [FeederCommandService.cs](src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder/Services/FeederCommandService.cs)

**Problem:** `/api/feeder/move`, `/api/feeder/program` เป็น endpoint สมมติ

**Fix:** รับ API spec จาก Feeder vendor แล้ว implement ให้ตรง

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

| Phase | งาน | ประมาณ |
|---|---|---|
| Critical Fixes | C1–C5 | 3–5 วัน |
| Security Hardening | H1–H3 | 3–5 วัน |
| Operational | M1–M5 | 5–7 วัน |
| Integration Testing | test cases ครบ | 5–7 วัน |
| Load / Stress Testing | verify NFR (500 orders/min) | 2–3 วัน |
| **รวม** | | **~4 สัปดาห์** |
