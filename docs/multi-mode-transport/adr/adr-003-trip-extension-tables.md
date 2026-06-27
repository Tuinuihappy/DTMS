# ADR-003: Trip Extension Tables (per-mode side tables)

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [ADR-001 Multi-Mode Split](adr-001-multi-mode-transport-split.md), [ADR-002 Facility/Station](adr-002-facility-station-hierarchy.md)

## Context

ปัจจุบัน [Trip.cs](../../../src/Modules/Dispatch/DTMS.Dispatch.Domain/Entities/Trip.cs) ฝัง RIOT3-specific fields:

```csharp
public class Trip {
    // core (mode-agnostic)
    public Guid Id, OrderId;
    public TripStatus Status;
    public DateTime CreatedAt, StartedAt?, CompletedAt?;
    ...

    // RIOT3-specific (จะ null สำหรับ Manual/Fleet)
    public string? VendorOrderKey;
    public string? VendorVehicleKey;
    public string? VendorVehicleName;
    public VendorPauseSource? VendorPauseSource;
}
```

หลังเพิ่ม Manual + Fleet จะมีฟิลด์เพิ่ม:
- Manual: `AssignedOperatorId`, `OperatorShiftId`, `GeofenceVerifiedAt`, `PodSignatureUrl`
- Fleet: `ProviderRef`, `WaybillNumber`, `TrackingUrl`, `EstimatedArrivalAt`

ถ้าใส่ใน Trip ทั้งหมด → ~12 nullable columns ที่ใช้แค่ตอน mode ตรงกัน

## Decision

แยกฟิลด์ mode-specific ออกเป็น **extension tables (1:0..1 with Trip)**:

```
Trip (core, mode-agnostic)
├── 1:0..1 → AmrTripExtension       (in Transport.Amr schema)
├── 1:0..1 → ManualTripExtension    (in Transport.Manual schema)
└── 1:0..1 → FleetTripExtension     (in Transport.Fleet schema)
```

### Trip Core (After)

```csharp
public class Trip {
    public Guid Id, OrderId;
    public TransportMode TransportMode { get; }    // discriminator
    public TripStatus Status;
    public DateTime CreatedAt;
    public DateTime? StartedAt, CompletedAt;
    public Guid PickupFacilityId, DropFacilityId;
    public Guid? PickupStationId, DropStationId;   // AMR-only
    public Guid? VehicleId;                        // optional binding to Vehicle aggregate
    // NO vendor-specific fields
}
```

### Extension Tables

```csharp
// in Transport.Amr/Domain/Entities/
public class AmrTripExtension {
    public Guid TripId { get; }                   // PK/FK
    public string? VendorOrderKey { get; }
    public string? VendorVehicleKey { get; }
    public string? VendorVehicleName { get; }
    public VendorPauseSource? VendorPauseSource { get; }
    public string? VendorRequestSnapshot { get; } // JSON
}

// in Transport.Manual/Domain/Entities/
public class ManualTripExtension {
    public Guid TripId { get; }
    public Guid AssignedOperatorId { get; }
    public Guid OperatorShiftId { get; }
    public DateTime AcknowledgedAt;
    public DateTime? PickupVerifiedAt;            // geofence verified
    public DateTime? DropVerifiedAt;
    public string? PodPhotoUrl;
    public string? PodSignatureUrl;
    public DateTime ExpectedAckBy;                // SLA deadline
    public DateTime ExpectedPickupBy;
    public DateTime ExpectedDropBy;
}

// in Transport.Fleet/Domain/Entities/
public class FleetTripExtension {
    public Guid TripId { get; }
    public string ProviderName { get; }           // "kerry", "flash"
    public string ProviderRef { get; }            // provider's reference
    public string WaybillNumber { get; }
    public string? TrackingUrl { get; }
    public DateTime? EstimatedArrivalAt { get; }
    public string? ProviderSnapshot { get; }      // JSON, last known status
}
```

### Repository Pattern

```csharp
// in Transport.Abstractions (or each module's Application)
public interface IAmrTripExtensionRepository {
    Task<AmrTripExtension?> GetByTripIdAsync(Guid tripId, CancellationToken ct);
    Task AddAsync(AmrTripExtension ext, CancellationToken ct);
    Task UpdateAsync(AmrTripExtension ext, CancellationToken ct);
}
// (similar for Manual + Fleet)
```

### Read pattern

```csharp
// Pause/Resume handler — agnostic to mode
public async Task Handle(PauseTripCommand cmd) {
    var trip = await _trips.GetByIdAsync(cmd.TripId);
    var vendorOps = _router.For(trip);              // resolves per trip.TransportMode
    var outcome = await vendorOps.PauseAsync(trip.Id, ct);
    trip.MarkPaused(outcome);
    await _trips.UpdateAsync(trip);
}

// Inside Transport.Amr — Riot3VendorEnvelopeOperationAdapter
public async Task<VendorOperationOutcome> PauseAsync(Guid tripId, CancellationToken ct) {
    var ext = await _amrTripExt.GetByTripIdAsync(tripId, ct);
    if (ext?.VendorOrderKey is null) return VendorOperationOutcome.NoVendorRecord;
    return await _riot3.PauseEnvelopeAsync(ext.VendorOrderKey, ct);
}
```

### Projection Strategy

Dispatch projections (`TripStatusHistoryRow`, `TripFactsRow`, `TripItemsRow`) อ่านจาก Trip core เท่านั้น — ฟิลด์ vendor-specific ถ้าต้องการ ให้ projection ของ Transport.Amr/Manual/Fleet สร้างของตนเอง (`AmrTripDetailRow`, etc.) แล้ว join ตอน query

## Alternatives Considered

### Alternative A: Keep all fields nullable on Trip

ใส่ทุก mode-specific fields บน Trip ทั้งหมด

**Pros:** 1 query โหลด trip ครบทุก field, ไม่มี join
**Cons:**
- Trip table อ้วน (~30+ columns), ฟิลด์เปล่าเยอะมาก
- Schema เปลี่ยนทุกครั้งที่เพิ่ม mode → Dispatch ต้องแก้
- Validation logic ต้อง enforce "ถ้า mode == Amr ต้องมี VendorOrderKey แต่ห้ามมี OperatorId" — เปลือง

**Rejected because:** ละเมิด single responsibility ของ Trip aggregate

### Alternative B: EF Owned Entities

ใช้ `[Owned]` หรือ `OwnsOne()` ใส่ AmrTripExt as owned entity ของ Trip

**Pros:** Single query, navigation property natural
**Cons:**
- Owned entity ต้อง register ใน Trip DbContext → Dispatch ต้องรู้จัก AMR/Manual/Fleet schemas
- Cross-module schema dependency
- ทำลาย module boundary ของ ADR-001

**Rejected because:** ขัดกับ ADR-001 (Dispatch ห้ามรู้จัก Transport.*)

### Alternative C: EF Table-Per-Hierarchy (TPH)

`AmrTrip : Trip`, `ManualTrip : Trip`, `FleetTrip : Trip` — single table with discriminator

**Pros:** Standard EF pattern, single query
**Cons:**
- Same as Alternative A — single fat table
- Inheritance ทำให้ projections + repository pattern ซับซ้อน
- Polymorphic queries มี caveat ใน EF Core 8+ (filter by type performance)

**Rejected because:** Same downsides as Alt A + inheritance complexity

## Consequences

### Positive

- ✓ Trip core สะอาด — เพิ่ม mode ใหม่ไม่ต้องแก้ schema Dispatch
- ✓ Extension tables เป็นของ module ตนเอง — boundary ชัดเจน
- ✓ Mode-specific projections เป็นไปได้ (e.g., AmrDispatchStatsView)
- ✓ Foreign key + cascade delete ทำงานปกติ (extension ต้องอยู่กับ Trip)

### Negative

- ✗ Query Trip + Extension ต้อง explicit join (1 extra round-trip ถ้าไม่ใช้ Include — แต่ negligible)
- ✗ Cross-module repository — Transport.Amr.Infrastructure ต้องมี AmrTripExtensionRepository
- ✗ Migration complexity — drop columns จาก Trip + create AmrTripExtension table

### Neutral

- AcceptDelete cascade: ถ้า Trip ถูกลบ extension ก็ลบตาม (one-way)
- Projections (TripFactsRow etc.) อยู่ที่ Dispatch — เก็บ TransportMode discriminator พอ, ไม่ดึง extension data เข้า projection

## Query Patterns (Performance Considerations)

### Pattern 1: Trip List View (Mode-Agnostic)

Dispatcher console list shows all trips — does NOT need extension data:

```csharp
// Dispatch.Application — most common query
var trips = await _ctx.Trips
    .Where(t => t.Status == TripStatus.InProgress)
    .Select(t => new TripListItem(t.Id, t.OrderId, t.TransportMode, t.Status, ...))
    .ToListAsync();
// → simple query on Trip table only, NO joins
```

### Pattern 2: Trip Detail View (Mode-Specific)

Detail page lazy-loads extension based on Trip.TransportMode:

```csharp
// Dispatch.Presentation → returns trip core
GET /api/dispatch/trips/{id}
→ Trip { id, mode, status, ... }

// Mode-specific detail loaded by frontend separately
GET /api/transport-amr/trips/{id}/details   (only if mode=Amr)
→ AmrTripExtension { vendorOrderKey, vendorVehicleKey, ... }

// or in one round-trip via composition endpoint:
GET /api/dispatch/trips/{id}?include=extensions
→ Composes from registered IExtensionProvider per mode
```

### Pattern 3: Operator-Facing Query (Manual Only)

Operator app queries own trips — natural to join Manual extension:

```csharp
// Transport.Manual.Application — operator queries
var myTrips = await _ctx.Trips
    .Join(_ctx.ManualTripExtensions,
          t => t.Id, ext => ext.TripId,
          (t, ext) => new { Trip = t, Ext = ext })
    .Where(x => x.Ext.AssignedOperatorId == operatorId)
    .Where(x => x.Trip.Status == TripStatus.InProgress)
    .ToListAsync();
// → uses index on ManualTripExtension.AssignedOperatorId
```

### Pattern 4: BI Aggregation (Cross-Mode)

BI reports group by mode without touching extensions:

```sql
-- TripFactsRow projection populated by Dispatch projector
-- Already has TransportMode column → no join needed for aggregations
SELECT
    transport_mode,
    DATE(completed_at) AS day,
    COUNT(*) AS total_trips,
    AVG(duration_seconds) AS avg_duration
FROM dispatch.trip_facts
WHERE completed_at >= '2026-06-01'
GROUP BY transport_mode, DATE(completed_at);
```

For mode-specific KPIs (e.g., "operator efficiency by warehouse"), build per-mode projections in respective modules.

### Index Strategy

```sql
-- Per-mode extension tables — index on common query keys
CREATE INDEX ix_amr_trip_ext_vendor_order_key
  ON transport_amr.amr_trip_extensions(vendor_order_key)
  WHERE vendor_order_key IS NOT NULL;
-- ↑ used by webhook to find Trip by vendor_order_key

CREATE INDEX ix_manual_trip_ext_operator
  ON transport_manual.manual_trip_extensions(assigned_operator_id);
-- ↑ used by operator app "my trips" query

CREATE INDEX ix_manual_trip_ext_sla
  ON transport_manual.manual_trip_extensions(expected_pickup_by, expected_drop_by)
  WHERE pickup_verified_at IS NULL OR drop_verified_at IS NULL;
-- ↑ used by SlaWatchdog to find stalled trips

CREATE INDEX ix_fleet_trip_ext_provider_ref
  ON transport_fleet.fleet_trip_extensions(provider_id, waybill_number);
-- ↑ used by webhook to find Trip by provider reference
```

## Edge Cases & Failure Modes

### Edge Case 1: Extension Missing for Existing Trip

Scenario: Trip exists, but extension row missing (data error or partial migration)

**Behavior:**
- `IAmrTripExtensionRepository.GetByTripIdAsync` returns null
- Pause/Resume adapter returns `NoVendorRecord`
- Trip transitions to `Failed` with reason "vendor record lost"
- Audit log captures incident for ops investigation

**Prevention:** Migration script transactional; integration test ensures dispatch always creates extension

### Edge Case 2: Extension Created Without Trip

Scenario: Extension INSERT succeeds, Trip INSERT fails (shouldn't happen with FK + transaction, but defensive)

**Prevention:**
- FK constraint: `extension.trip_id REFERENCES dispatch.trips(id)` blocks insert
- `ON DELETE CASCADE`: if Trip deleted, extension cleaned up automatically
- All dispatch flows wrap Trip + Extension creation in single transaction

### Edge Case 3: Mode Change Attempted Post-Creation

Scenario: Admin tries to convert Manual trip → AMR trip

**Behavior:** Blocked by Trip aggregate (TransportMode immutable). See [ADR-001 Edge Case 4](adr-001-multi-mode-transport-split.md#edge-case-4-cross-mode-trip-conversion)

If business requires conversion: cancel + recreate pattern

### Edge Case 4: Multiple Extensions Created Erroneously

Scenario: Race condition creates 2 AmrTripExtensions for same TripId

**Prevention:**
- PK on `TripId` (not auto-generated Id) → duplicate insert throws
- Repository `AddAsync` first checks `GetByTripIdAsync` and throws if exists
- Database constraint is final defense

### Edge Case 5: Schema Migration Order

Scenario: Dispatch module migration drops `VendorOrderKey` column BEFORE Transport.Amr migration creates `amr_trip_extensions`

**Mitigation:**
- Per [memory `project_shared_migration_history`](../../memory/project_shared_migration_history.md): MigrationId timestamps unique cross-module
- Phase 3 migration order:
  1. `20260712000000_CreateAmrTripExtensions` (Transport.Amr) — creates table
  2. `20260712000001_BackfillVendorFieldsToExt` (Transport.Amr) — copies data
  3. `20260712000002_StripVendorFieldsFromTrip` (Dispatch) — drops columns
- Each migration includes existence check (idempotent)

## Schema-Level Boundaries

Per-mode schemas enforce data isolation:

```
public schema:
  __EFMigrationsHistory     (shared, per memory feedback_migration_manual)

dispatch schema:
  trips, trip_facts, trip_items, trip_status_history

transport_amr schema:
  amr_trip_extensions, amr_units, amr_maps, amr_stations, charging_policies

transport_manual schema:
  manual_trip_extensions, operators, operator_shifts, operator_devices, refresh_tokens

transport_fleet schema:
  fleet_trip_extensions, providers, contracts, waybills
```

Cross-schema FK allowed **only** from extension → dispatch.trips (one direction, intentional)

## Repository Lifetime Considerations

```csharp
// in Transport.Amr.Infrastructure
public sealed class AmrTripExtensionRepository : IAmrTripExtensionRepository
{
    private readonly TransportAmrDbContext _ctx;   // separate DbContext from Dispatch

    public AmrTripExtensionRepository(TransportAmrDbContext ctx) => _ctx = ctx;

    public Task AddAsync(AmrTripExtension ext, CancellationToken ct)
        => _ctx.AmrTripExtensions.AddAsync(ext, ct).AsTask();
}
```

**Important:** Each Transport.* module has **its own DbContext** to enforce schema boundary. Both DbContexts point to same physical database, different schemas.

Transaction across modules: use `IDbContextTransaction` shared via `IUnitOfWork` abstraction or **MassTransit outbox** pattern (already in use per existing infrastructure).

## Migration (Pre-launch)

```
Phase 3 Migration Sequence:

1. Create new tables (Transport.Amr first):
   - transport_amr.amr_trip_extensions (TripId PK/FK + cascade delete)

2. (Phase 4) Create:
   - transport_manual.manual_trip_extensions

3. (Phase 5) Create:
   - transport_fleet.fleet_trip_extensions

4. Migrate existing data (Phase 3, if any AMR trips exist):
   INSERT INTO transport_amr.amr_trip_extensions
     (trip_id, vendor_order_key, vendor_vehicle_key, ...)
   SELECT id, vendor_order_key, vendor_vehicle_key, ...
   FROM dispatch.trips
   WHERE vendor_order_key IS NOT NULL;

5. Drop columns from dispatch.trips (Phase 3):
   - vendor_order_key, vendor_vehicle_key, vendor_vehicle_name, vendor_pause_source
```

(Pre-launch — reset DB acceptable, no production data to backfill)

## Related ADRs

- [ADR-001](adr-001-multi-mode-transport-split.md) — Module split (parent decision)
- [ADR-002](adr-002-facility-station-hierarchy.md) — Warehouse/Station split (separate axis)
- [ADR-004](adr-004-testing-strategy.md) — How to test extension repository round-trips
- [ADR-008](adr-008-migration-strategy.md) — Migration sequence including extension table creation

## References

- DDD aggregate boundary (Eric Evans)
- Per-tenant extension tables pattern (multi-tenant SaaS)
- EF Core relationships: https://learn.microsoft.com/ef/core/modeling/relationships
- Existing Trip aggregate: [Trip.cs](../../../src/Modules/Dispatch/DTMS.Dispatch.Domain/Entities/Trip.cs)
