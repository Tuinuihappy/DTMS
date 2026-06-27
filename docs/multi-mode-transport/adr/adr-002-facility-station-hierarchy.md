# ADR-002: Facility/Station Hierarchy

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [ADR-001 Multi-Mode Split](adr-001-multi-mode-transport-split.md)

## Context

ปัจจุบัน `Station` entity ที่ [Station.cs](../../../src/Modules/Facility/DTMS.Facility.Domain/Entities/Station.cs) เป็น **AMR-specific โดยพฤตินัย** แต่ตั้งอยู่ใน Facility module ที่ควรเป็น mode-agnostic:

```csharp
public class Station {
    public Guid MapId;                   // ผูกกับ RIOT3 Map
    public Coordinate Coordinate;        // x, y, theta (factory-local)
    public string? VendorRef;            // RIOT3 station ID
    public Dictionary Actions;           // RIOT3 ACT mission config
}
```

Manual / Fleet mode ต้องการ "place where pickup/drop happens" ในระดับที่ต่างกัน:

| มิติ | AMR Station (RIOT3) | Manual/Fleet Station |
|---|---|---|
| **ความหมาย** | จุดเฉพาะใน warehouse (dock, charge, lift) | อาคาร/คลังทั้งหลัง ("Warehouse A") |
| **Granularity** | ละเอียดมาก (~50 stations/warehouse) | หยาบ (1 warehouse = 1-2 stations) |
| **Coordinate** | Factory-local x/y/theta | Global lat/lng |
| **Source of truth** | RIOT3 (sync via VendorRef) | DTMS-owned |
| **Address** | ไม่มี | ต้องมี (delivery contact) |
| **Geofence** | ไม่ต้อง | ต้อง (Manual GPS verify) |

ถ้ายัด Manual/Fleet ใน Station entity ปัจจุบัน:
- ✗ ต้อง create dummy `Map` ให้ทุก warehouse (Map = RIOT3 concept)
- ✗ Coordinate (x,y,theta) เป็น noise
- ✗ VendorRef null ตลอด
- ✗ ไม่มีที่ใส่ lat/lng + address + contact
- ✗ Semantic ambiguous — "WH-A" คือคลังหรือ dock ในคลัง?

## Decision

แยกเป็น **2-level hierarchy**: `Facility` (warehouse) ↔ `AmrStation` (dock inside warehouse, AMR-only)

```
Facility (Warehouse/Site)              ← Modules/Facility (shared)
├── Id, Name, lat, lng
├── Address, ContactInfo, OperatingHours
├── GeofenceRadiusM | GeofenceArea
├── ServiceModes: [Amr, Manual, Fleet]    ← facility นี้รองรับ mode ไหนบ้าง
└── (no Stations field — AmrStation อยู่ที่ Transport.Amr)

AmrMap (RIOT3 floor plan)              ← Modules/Transport.Amr (AMR-only)
├── FacilityId (FK to Facility)
├── VendorRef (RIOT3 map ID)
└── Stations: AmrStation[]

AmrStation                             ← Modules/Transport.Amr (AMR-only)
├── MapId (FK to AmrMap)
├── FacilityId (denorm FK)             ← ใช้ใน query
├── Coordinate (x, y, theta)
├── VendorRef (RIOT3 station ID)
├── Actions, CompatibleVehicleTypes
└── Type: {Normal, Charging, Pickup, Dropoff, Parking, Dock, Checkpoint}
```

### Item/Trip Reference Pattern

```csharp
// Item — every order picks/drops at a Facility; only AMR specifies Station
public class Item {
    public Guid PickupFacilityId;         // required (every mode)
    public Guid? PickupStationId;         // required for AMR, null for Manual/Fleet
    public Guid DropFacilityId;
    public Guid? DropStationId;
}

// Trip — snapshot at create time
public class Trip {
    public Guid PickupFacilityId;
    public Guid? PickupStationId;
    public Guid DropFacilityId;
    public Guid? DropStationId;
}
```

### Invariants

- ทุก Item/Trip ต้องมี `FacilityId` (pickup + drop)
- ถ้า `Order.TransportMode == Amr` → `StationId required`
- ถ้า `Order.TransportMode in [Manual, Fleet]` → `StationId optional` (ถ้าระบุก็ใช้ได้ — wayfinding ภายใน warehouse)

## Alternatives Considered

### Alternative A: Unified Station with optional fields per mode

เพิ่ม `LatLng?`, `Address?`, `GeofenceRadiusM?` ที่ Station; ใช้ `CompatibleVehicleTypes[]` filter

**Pros:** 1 entity, 1 selector, schema change น้อย
**Cons:**
- nullable fields เยอะ — semantic ambiguous
- "Station" name ขัดกับสิ่งที่ user คิดสำหรับ warehouse
- Map + Coordinate ยังคงไม่มีความหมายสำหรับ Manual/Fleet

**Rejected because:** semantic clarity สำคัญกว่า — warehouse กับ dock เป็นคนละ concept แม้จะมี relation

### Alternative B: Per-mode Station hierarchies (no shared parent)

`AmrStation` ใน Transport.Amr, `ManualLocation` ใน Transport.Manual, `FleetDeliveryAddress` ใน Transport.Fleet

**Pros:** Clean module isolation
**Cons:**
- ไม่มี shared concept — "Warehouse A serves all 3 modes" ทำไม่ได้
- BI report cross-mode (e.g., "งานทั้งหมดที่ Warehouse A") ต้อง union 3 tables
- Frontend ต้องมี 3 pickers

**Rejected because:** Warehouse คือ shared business concept — ควรอยู่ใน shared kernel

## Consequences

### Positive

- ✓ Semantic clarity: Facility = warehouse/building, AmrStation = dock inside
- ✓ Single Facility serves multiple modes (AMR dock + Manual receiving bay ในตึกเดียวกัน)
- ✓ Geofence + address พร้อมใช้สำหรับ Manual mode
- ✓ AMR-specific concepts (Map, Coordinate, VendorRef) อยู่ที่ที่ควรอยู่
- ✓ Cross-mode query ทำได้ผ่าน `FacilityId` (shared anchor)

### Negative

- ✗ Breaking schema change: drop Station from Facility schema, create Warehouse + recreate AmrStation at Transport.Amr schema
- ✗ ทุก Item/Trip/OrderTemplate ต้องเพิ่ม WarehouseId column + backfill (pre-launch — OK)
- ✗ Frontend `StationCombobox` → 2-step picker (Warehouse → AmrStation)
- ✗ `MapStationSyncService` ต้อง resolve FacilityId เพิ่มตอน import จาก RIOT3

### Neutral

- ChargingPolicy + AmrUnit ตามมา (related to ADR-001 Vehicle split)
- Facility ที่ไม่มี AMR ก็ยังใช้ system ได้ (no AmrMap, no AmrStations)

## Data Model

### Facility (in `Modules/Facility/Domain/Entities/`)

```csharp
public class Facility {
    public Guid Id { get; }
    public string Name { get; }
    public string Code { get; }              // human label, e.g. "WH-BKK-01"
    public LatLng Location { get; }
    public Address Address { get; }
    public int? GeofenceRadiusM { get; }
    public Polygon? GeofenceArea { get; }    // alternative to radius
    public ContactInfo? PrimaryContact { get; }
    public OperatingHours Hours { get; }
    public IReadOnlyCollection<TransportMode> ServiceModes { get; }
    public bool IsActive { get; }
}
```

### AmrMap, AmrStation (in `Modules/Transport.Amr/Domain/Entities/`)

```csharp
public class AmrMap {
    public Guid Id { get; }
    public Guid FacilityId { get; }          // FK to Facility
    public string VendorRef { get; }          // RIOT3 map ID
    public string Name { get; }
    public DateTime? LastSyncedAt { get; }
}

public class AmrStation {
    public Guid Id { get; }
    public Guid MapId { get; }                // FK to AmrMap
    public Guid FacilityId { get; }           // denormalized FK to Facility (query optimization)
    public string Code { get; }
    public string? VendorRef { get; }         // RIOT3 station ID
    public Coordinate Coordinate { get; }
    public StationType Type { get; }
    public IReadOnlyDictionary<string, StationAction> Actions { get; }
    public IReadOnlyList<string> CompatibleVehicleTypes { get; }
    public bool IsActive { get; }
    // (existing ManualOverrideOffline fields preserved)
}
```

### FacilityResource Handling

Existing `FacilityResource` (doors, elevators, chargers) ต้องแบ่ง:

| Resource type | Goes to | Reason |
|---|---|---|
| **Doors, gates** (generic) | `Modules/Facility` | All modes care (security, access control) |
| **Elevators** (with RIOT3 control) | `Modules/Transport.Amr` | Robot uses RIOT3 protocol to call elevator |
| **Chargers** (robot-specific) | `Modules/Transport.Amr` | AMR-only; Manual/Fleet trucks don't charge here |
| **Loading bays** (generic + AMR-specific Actions) | `Modules/Facility` + reference from AmrStation | Bay itself is shared; Actions are AMR-only |

Rule: ถ้า resource มี `VendorRef` (RIOT3 ID) → Transport.Amr. ถ้าไม่มี → Facility.

### Geofence: Circle vs Polygon

`Facility` exposes both `GeofenceRadiusM` (circle) AND `GeofenceArea` (polygon — GeoJSON or PostGIS):

- **Circle** (radius) — simpler, ใช้สำหรับ warehouse ที่เป็นจุดเดียว (small site)
- **Polygon** — สำหรับ warehouse ใหญ่ที่มีรูปร่างซับซ้อน (DC ขนาดใหญ่ + พื้นที่จอด + เขตห้าม)

Validation logic:
```csharp
public bool IsInsideGeofence(LatLng point)
{
    if (GeofenceArea is not null)
        return GeofenceArea.Contains(point);   // PostGIS ST_Contains
    if (GeofenceRadiusM is not null)
        return DistanceMeters(Location, point) <= GeofenceRadiusM.Value;
    return true;   // no geofence configured = no enforcement
}
```

Both null = no geofence enforcement (e.g., customer addresses for Fleet mode delivery to end-customer)

### ManualOverrideOffline (Preserved from Existing Station)

Current `Station` has operator-driven force-offline (survives RIOT3 sync). This belongs to **AmrStation** only (RIOT3 concept):

```csharp
public class AmrStation
{
    // ... other fields
    public bool ManualOverrideOffline { get; private set; }
    public string? ManualOverrideReason { get; private set; }
    public string? ManualOverrideBy { get; private set; }
    public DateTime? ManualOverrideAt { get; private set; }
    public DateTime? ManualOverrideExpiresAt { get; private set; }

    public void ForceOffline(string reason, string by, TimeSpan? duration) { ... }
    public void ClearOverride() { ... }
}
```

Warehouse has separate `IsActive` flag (mode-agnostic active/inactive)

## Edge Cases & Failure Modes

### Edge Case 1: Warehouse Without AMR Map

Scenario: Customer warehouse (drop destination for Fleet mode) — no RIOT3 floor plan ever needed

**Handling:**
- Warehouse exists with `ServiceModes = [Fleet]`
- No AmrMap created (NULL safe)
- Order validation: `if mode == Fleet, no station required` — passes
- AmrMapStationSync service skips warehouses without `ServiceModes.Contains(Amr)`

### Edge Case 2: Warehouse Newly Adds AMR Support

Scenario: Existing Manual warehouse adopts robots

**Handling:**
1. Update `Warehouse.ServiceModes` to include `Amr`
2. Import AMR floor plan: `POST /api/transport-amr/maps/import { warehouseId, riot3MapId }`
3. AmrMapStationSync picks up + creates AmrStations
4. New AMR orders can now select this warehouse

### Edge Case 3: Removing a Warehouse with Active Trips

Scenario: Decommission warehouse, but trips still in-flight

**Handling:**
- Set `Warehouse.IsActive = false` (don't delete)
- New order creation: filter `WarehouseLookup` by `IsActive = true`
- Existing in-flight trips: continue (warehouse data still readable)
- Hard delete: only after all related trips reach terminal state
- Cascade behavior: `Warehouse.Delete()` not allowed if any `Trip.PickupWarehouseId == Id OR DropWarehouseId == Id`

### Edge Case 4: AmrStation Code Conflict with Warehouse Code

Scenario: User names AmrStation "WH-A" same as Warehouse code "WH-A"

**Handling:**
- Different unique constraints, different lookup APIs
- Warehouse codes unique globally
- AmrStation codes unique per-Map (which is per-Warehouse)
- No semantic confusion: 2-step picker shows different UI for each level
- Frontend: validate code patterns separately (e.g., Warehouses use "WH-*", Stations use "DOCK-*")

### Edge Case 5: GPS Coordinate System Mismatch

Scenario: AmrStation `Coordinate` (factory-local) vs Warehouse `Location` (lat/lng) — operator app sees both?

**Handling:**
- Operator app only sees `Warehouse.Location` (lat/lng) — never factory-local
- AmrStation.Coordinate consumed by RIOT3 only (server-to-server)
- Frontend map (dispatcher console): renders AmrStation positions inside warehouse floor plan (separate canvas), not on global map
- Operator app map: global map with warehouse pin only

## Frontend Implications

### 2-Step Picker Behavior

```tsx
<OrderForm>
  <WarehouseCombobox
    value={pickupWarehouseId}
    onChange={setPickupWarehouseId}
    filterByServiceMode={transportMode}    // only show Warehouses serving this mode
  />

  {transportMode === 'Amr' && pickupWarehouseId && (
    <AmrStationCombobox
      warehouseId={pickupWarehouseId}
      value={pickupStationId}
      onChange={setPickupStationId}
      filterByType="Pickup"               // only pickup stations, not charge/dock
    />
  )}
</OrderForm>
```

### Search Hierarchy

User search "BKK" should match:
- Warehouse: `code` (WH-BKK-*) OR `name` ("Bangkok DC")
- AmrStation: `code` only (DOCK-01, CHARGE-02)
- Show warehouse hits before station hits in autocomplete

### Permissions

| Action | Required Role |
|---|---|
| Create/edit Warehouse | Admin |
| Sync AMR map | Admin or Operations Manager |
| Override AmrStation offline | Dispatcher or Operations Manager |
| Read Warehouse / AmrStation | Any authenticated user |

## Migration

Pre-launch (per acceptance criteria) — schema reset is acceptable. ดู [Phase 2 doc](../phases/phase-2-facility-vehicle-split.md) สำหรับ migration script.

### Data Migration Order (within Phase 2)

1. Create `facility.warehouses` table
2. Backfill from existing `facility.stations` (group by MapId → 1 Warehouse per Map)
   - Use `Map.VendorRef` mapping to RIOT3 site name as Warehouse.Name
   - lat/lng default to centroid of stations (require manual update after)
3. Create `transport_amr.amr_maps` from existing `facility.maps` (preserve IDs)
4. Create `transport_amr.amr_stations` from existing `facility.stations` (preserve IDs)
5. Add `WarehouseId` FK to Item, Trip, OrderTemplate
6. Backfill `WarehouseId` from existing station references
7. Drop old `facility.stations` and `facility.maps`

Backfill script (run before Drop):
```sql
-- Pre-Drop: capture existing relationships
INSERT INTO facility.warehouses (id, code, name, lat, lng, service_modes, is_active, created_at)
SELECT
    gen_random_uuid() AS id,
    'WH-' || UPPER(SUBSTRING(m.name FROM 1 FOR 10)) AS code,
    m.name,
    AVG(s.coordinate_x)::DOUBLE PRECISION AS lat,    -- placeholder; manual fix after
    AVG(s.coordinate_y)::DOUBLE PRECISION AS lng,
    '["Amr"]'::jsonb AS service_modes,
    TRUE,
    NOW()
FROM facility.maps m
LEFT JOIN facility.stations s ON s.map_id = m.id
GROUP BY m.id, m.name;
```

## Related ADRs

- [ADR-001](adr-001-multi-mode-transport-split.md) — Module split (parent decision)
- [ADR-003](adr-003-trip-extension-tables.md) — Trip schema after this split
- [ADR-007](adr-007-mobile-api-authentication.md) — Warehouse scope claim in operator JWT
- [ADR-008](adr-008-migration-strategy.md) — Schema migration sequence for this refactor
- [ADR-010](adr-010-geofence-implementation.md) — How geofence (radius / polygon) calculation works

## References

- DDD: Bounded Context separation (Eric Evans)
- DTMS existing `IStationLookup`: [FacilityStationLookup.cs](../../../src/Modules/Facility/DTMS.Facility.Infrastructure/Services/FacilityStationLookup.cs)
