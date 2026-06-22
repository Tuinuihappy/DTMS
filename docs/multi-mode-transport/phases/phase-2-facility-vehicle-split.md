# Phase 2 — Facility / Vehicle Split

- **Sprint**: 3-4
- **Risk**: High (schema breaking + multi-module impact)
- **Schema change**: Yes (breaking, pre-launch reset OK)
- **Frontend impact**: Yes (Order create/edit, dispatch filters)
- **Depends on**: [Phase 1](phase-1-foundation.md)

## Goal

แยกเป็น 2 axes:
1. **Facility split**: Promote Warehouse เป็น first-class ใน Facility module; ย้าย AMR-specific Map/Station/Coordinate ไป Transport.Amr
2. **Vehicle split**: Strip Battery/Charging จาก Vehicle core; ย้ายไป AmrUnit ใน Transport.Amr

## Task Checklist

### Step 1: Create Warehouse Aggregate

**`src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Warehouse.cs`** (NEW):

```csharp
namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class Warehouse
{
    public Guid Id { get; private set; }
    public string Code { get; private set; }                    // "WH-BKK-01"
    public string Name { get; private set; }
    public LatLng Location { get; private set; }
    public Address Address { get; private set; }
    public int? GeofenceRadiusM { get; private set; }
    public ContactInfo? PrimaryContact { get; private set; }
    public OperatingHours Hours { get; private set; }
    public IReadOnlyList<TransportMode> ServiceModes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Warehouse() { }

    public static Warehouse Create(...) { ... }
    public void UpdateGeofence(int radiusM) { ... }
    public void EnableMode(TransportMode mode) { ... }
    public void Deactivate() { ... }
}
```

### Step 2: Create Value Objects (Facility module)

**`src/Modules/Facility/.../ValueObjects/`**:
- `LatLng.cs` (Lat, Lng)
- `Address.cs` (Street, City, State, PostalCode, Country)
- `ContactInfo.cs` (Name, Phone, Email)
- `OperatingHours.cs` (Mon-Sun open/close times)

### Step 3: Move AMR Entities to Transport.Amr

```bash
git mv src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Map.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/Entities/AmrMap.cs

git mv src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Station.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/Entities/AmrStation.cs

git mv src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Coordinate.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/ValueObjects/Coordinate.cs

git mv src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/RouteEdge.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/Entities/AmrRouteEdge.cs

git mv src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/Zone.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/Entities/AmrZone.cs

git mv src/Modules/Facility/AMR.DeliveryPlanning.Facility.Domain/Entities/StationAction.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/ValueObjects/StationAction.cs
```

### Step 4: Rename Classes + Add FacilityId

**AmrMap.cs** — เพิ่ม FK:
```csharp
public class AmrMap  // ← renamed from Map
{
    public Guid Id { get; }
    public Guid FacilityId { get; }              // ← NEW FK
    public string VendorRef { get; }              // RIOT3 map ID
    public string Name { get; }
    public DateTime? LastSyncedAt { get; }
    public IReadOnlyList<AmrStation> Stations { get; }
}
```

**AmrStation.cs** — เพิ่ม denormalized FK:
```csharp
public class AmrStation  // ← renamed from Station
{
    public Guid Id { get; }
    public Guid MapId { get; }
    public Guid FacilityId { get; }              // ← NEW denormalized FK (query optimization)
    public string Code { get; }
    public string? VendorRef { get; }
    public Coordinate Coordinate { get; }
    public StationType Type { get; }
    public IReadOnlyDictionary<string, StationAction> Actions { get; }
    public IReadOnlyList<string> CompatibleVehicleTypes { get; }
    public bool IsActive { get; }
    // (existing ManualOverrideOffline fields preserved)
}
```

### Step 5: Vehicle Split

**Rename**:
```bash
git mv src/Modules/Fleet src/Modules/Vehicle
git mv src/Modules/Vehicle/AMR.DeliveryPlanning.Fleet.Domain \
       src/Modules/Vehicle/AMR.DeliveryPlanning.Vehicle.Domain
# ... rename all Fleet sub-projects to Vehicle
```

**Strip Vehicle core** — `src/Modules/Vehicle/.../Entities/Vehicle.cs`:
```csharp
public class Vehicle
{
    public Guid Id { get; }
    public string RegistrationCode { get; }      // e.g. "FAN1_STANDARD_NO5"
    public Guid VehicleTypeId { get; }
    public VehicleStatus Status { get; }         // Available/InUse/OutOfService
    public Guid? OwnerOrganizationId { get; }
    public DateTime RegisteredAt { get; }
    // REMOVED: BatteryLevel, AdapterKey, VendorVehicleKey
    // REMOVED: VehicleState enum values: Charging, Maintenance (mode-specific)
}

public enum VehicleStatus { Available, InUse, OutOfService }  // simplified
```

**Move ChargingPolicy** → Transport.Amr:
```bash
git mv src/Modules/Vehicle/AMR.DeliveryPlanning.Vehicle.Domain/Entities/ChargingPolicy.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/Entities/ChargingPolicy.cs
```

**Create AmrUnit** — `src/Modules/Transport.Amr/.../Domain/Entities/AmrUnit.cs` (NEW):
```csharp
public class AmrUnit
{
    public Guid Id { get; }
    public Guid VehicleId { get; }               // FK to Vehicle (1:0..1)
    public string VendorVehicleKey { get; }      // RIOT3 deviceKey
    public string? VendorVehicleName { get; }
    public double BatteryLevel { get; }
    public Guid? ChargingPolicyId { get; }
    public AmrUnitState State { get; }           // Idle/Moving/Charging/Maintenance
    public DateTime? LastSeenAt { get; }
}
```

### Step 6: Update Item + Trip schema

**`src/Modules/DeliveryOrder/.../Entities/Item.cs`** — เพิ่ม `WarehouseId`:
```csharp
public class Item
{
    // existing
    public string PickupLocationCode { get; }
    public string DropLocationCode { get; }
    public Guid? PickupStationId { get; }
    public Guid? DropStationId { get; }

    // NEW
    public Guid PickupWarehouseId { get; private set; }  // required
    public Guid DropWarehouseId { get; private set; }    // required

    public void SetWarehouseIds(Guid pickup, Guid drop) { ... }
}
```

**Validation** — `IItemValidator`:
```csharp
public ValidationResult Validate(Item item, TransportMode mode)
{
    if (item.PickupWarehouseId == Guid.Empty)
        return Invalid("PickupWarehouseId required");

    if (mode == TransportMode.Amr && item.PickupStationId is null)
        return Invalid("PickupStationId required for AMR mode");

    // Manual/Fleet → station optional
    return Valid();
}
```

**`src/Modules/Dispatch/.../Entities/Trip.cs`** — เพิ่ม `WarehouseId`:
```csharp
public class Trip
{
    // existing
    public Guid? PickupStationId { get; private set; }
    public Guid? DropStationId { get; private set; }

    // NEW
    public Guid PickupWarehouseId { get; private set; }
    public Guid DropWarehouseId { get; private set; }
}
```

### Step 7: Update Repositories + Services

**Replace** `IStationLookup` ด้วย **2 separated** lookups:

**`src/Modules/Facility/.../Application/Services/IWarehouseLookup.cs`** (NEW):
```csharp
public interface IWarehouseLookup
{
    Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct);
    Task<Warehouse?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyDictionary<string, Guid>> ResolveBatchAsync(IEnumerable<string> codes, CancellationToken ct);
}
```

**`src/Modules/Transport.Amr/.../Application/Services/IAmrStationLookup.cs`** (NEW):
```csharp
public interface IAmrStationLookup
{
    Task<Guid?> ResolveByCodeAsync(Guid warehouseId, string code, CancellationToken ct);
    Task<AmrStation?> GetAsync(Guid id, CancellationToken ct);
}
```

DeleveryOrder validation flow:
```
1. ResolveByCodeAsync(PickupLocationCode) → WarehouseId
2. (if Amr mode) ResolveByCodeAsync(warehouseId, stationCode) → StationId
3. SetWarehouseIds + SetStationIds on Item
```

### Step 8: Move Background Services

ย้ายจาก `Program.cs` ไป `TransportAmrServiceCollectionExtensions.AddTransportAmr()`:
- `MapStationSyncService` → renamed `AmrMapStationSyncService`
- `RouteEdgeSyncService` → renamed `AmrRouteEdgeSyncService`
- `TopologyOverlayExpiryService`
- Update implementations: use `IWarehouseLookup` to resolve FacilityId during RIOT3 import

### Step 9: EF Migrations (Manual)

Per [memory feedback_migration_manual](../../memory/feedback_migration_manual.md), เขียน migration เอง:

**Facility schema** (new):
```sql
-- 20260628000000_PromoteWarehouseAggregate.cs (Facility.Infrastructure)
CREATE TABLE facility.warehouses (
    id UUID PRIMARY KEY,
    code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(200) NOT NULL,
    lat DOUBLE PRECISION NOT NULL,
    lng DOUBLE PRECISION NOT NULL,
    address_street VARCHAR(500), address_city VARCHAR(100), ...,
    geofence_radius_m INTEGER,
    contact_name VARCHAR(200), contact_phone VARCHAR(50), ...,
    service_modes JSONB NOT NULL DEFAULT '[]',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL
);

DROP TABLE facility.stations;       -- moved to transport_amr schema
DROP TABLE facility.maps;
DROP TABLE facility.route_edges;
DROP TABLE facility.zones;
```

**Transport.Amr schema** (new):
```sql
-- 20260628000001_CreateAmrSchema.cs (Transport.Amr.Infrastructure)
CREATE SCHEMA transport_amr;

CREATE TABLE transport_amr.amr_maps (
    id UUID PRIMARY KEY,
    facility_id UUID NOT NULL REFERENCES facility.warehouses(id),
    vendor_ref VARCHAR(200) NOT NULL,
    name VARCHAR(200) NOT NULL,
    ...,
    UNIQUE (facility_id, vendor_ref)
);

CREATE TABLE transport_amr.amr_stations (
    id UUID PRIMARY KEY,
    map_id UUID NOT NULL REFERENCES transport_amr.amr_maps(id),
    facility_id UUID NOT NULL REFERENCES facility.warehouses(id),
    code VARCHAR(50) NOT NULL,
    vendor_ref VARCHAR(200),
    coordinate_x DOUBLE PRECISION NOT NULL,
    coordinate_y DOUBLE PRECISION NOT NULL,
    coordinate_theta DOUBLE PRECISION,
    type VARCHAR(50) NOT NULL,
    actions JSONB NOT NULL DEFAULT '{}',
    compatible_vehicle_types JSONB NOT NULL DEFAULT '[]',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (map_id, code)
);

CREATE TABLE transport_amr.amr_route_edges (...);
CREATE TABLE transport_amr.charging_policies (...);

CREATE TABLE transport_amr.amr_units (
    id UUID PRIMARY KEY,
    vehicle_id UUID NOT NULL REFERENCES vehicle.vehicles(id) UNIQUE,
    vendor_vehicle_key VARCHAR(200) NOT NULL UNIQUE,
    vendor_vehicle_name VARCHAR(200),
    battery_level DOUBLE PRECISION,
    charging_policy_id UUID REFERENCES transport_amr.charging_policies(id),
    state VARCHAR(50) NOT NULL,
    last_seen_at TIMESTAMPTZ
);
```

**Vehicle (Fleet) schema** (modify):
```sql
-- 20260628000002_StripVehicleAmrFields.cs
ALTER SCHEMA fleet RENAME TO vehicle;

ALTER TABLE vehicle.vehicles
    DROP COLUMN battery_level,
    DROP COLUMN adapter_key,
    DROP COLUMN vendor_vehicle_key;

DROP TABLE vehicle.charging_policies;   -- moved to transport_amr
```

**Item/Trip schema** (modify):
```sql
-- 20260628000003_AddWarehouseFkToItem.cs (DeliveryOrder.Infrastructure)
ALTER TABLE delivery_order.items
    ADD COLUMN pickup_warehouse_id UUID NOT NULL REFERENCES facility.warehouses(id),
    ADD COLUMN drop_warehouse_id UUID NOT NULL REFERENCES facility.warehouses(id);

-- 20260628000004_AddWarehouseFkToTrip.cs (Dispatch.Infrastructure)
ALTER TABLE dispatch.trips
    ADD COLUMN pickup_warehouse_id UUID NOT NULL,
    ADD COLUMN drop_warehouse_id UUID NOT NULL;
```

**MigrationId**: Per [memory project_shared_migration_history](../../memory/project_shared_migration_history.md) — ตั้ง timestamp ห่างกัน 1-2 วินาที cross-module เพื่อหลีกเลี่ยง skip

### Step 10: Frontend Updates

**`frontend/lib/api/facility.ts`** — แยก endpoints:
```typescript
// before:
export async function getStationOptions(): Promise<StationOption[]> { ... }

// after:
export type WarehouseOption = { id: string; code: string; name: string; serviceModes: TransportMode[] };
export type AmrStationOption = { id: string; warehouseId: string; code: string; name: string; type: string };

export async function getWarehouses(): Promise<WarehouseOption[]> { ... }
export async function getAmrStations(warehouseId: string): Promise<AmrStationOption[]> { ... }
```

**`frontend/components/primitives/warehouse-combobox.tsx`** (NEW):
2-step picker — Warehouse first, then conditional AmrStation:

```tsx
<div>
  <WarehouseCombobox value={warehouseId} onChange={setWarehouseId} />
  {transportMode === 'Amr' && warehouseId && (
    <AmrStationCombobox warehouseId={warehouseId} value={stationId} onChange={setStationId} />
  )}
</div>
```

**Pages affected**:
- `frontend/app/orders/new/page.tsx`
- `frontend/app/orders/[id]/edit/page.tsx`
- `frontend/components/dispatch/trip-filter.tsx`
- Other places using existing `StationCombobox`

## Verification

### Build & Test

```bash
# Reset DB (pre-launch)
docker compose -f docker-compose.yml restart postgres
# Or drop schema manually

# Gate 1: Build
dotnet build --configuration Release

# Gate 2: Apply migrations
dotnet run --project src/AMR.DeliveryPlanning.Api
# (migrations apply on startup via Microsoft.EntityFrameworkCore Migrate())

# Gate 3: Tests
dotnet test --no-build --logger "console;verbosity=minimal"

# Gate 4: Architecture
dotnet test tests/ArchitectureTests/ --no-build

# Gate 5: Frontend
cd frontend && npm run typecheck && npm run lint && npm run build
```

### NEW Tests

```csharp
// tests/Modules/Facility.UnitTests/WarehouseTests.cs
[Fact]
public void Create_WithValidData_ProducesAggregate() { ... }

[Fact]
public void EnableMode_AddsToServiceModes() { ... }

// tests/Modules/Transport.Amr.UnitTests/AmrStationTests.cs
[Fact]
public void Constructor_RequiresFacilityIdMatchingMap() { ... }

// tests/Modules/DeliveryOrder.UnitTests/ItemValidationTests.cs
[Theory]
[InlineData(TransportMode.Amr, null, /* expect */ false)]   // Amr requires station
[InlineData(TransportMode.Manual, null, /* expect */ true)] // Manual station optional
public void Validate_StationIdRequirement(TransportMode mode, Guid? stationId, bool expected) { ... }

// tests/ArchitectureTests/ModuleBoundaryTests.cs
[Fact]
public void FacilityModule_ShouldNotReferenceTransportAmr() {
    typeof(Warehouse).Assembly.GetReferencedAssemblies()
        .Select(a => a.Name)
        .Should().NotContain("AMR.DeliveryPlanning.Transport.Amr");
}
```

### Manual Smoke Test

```
1. Seed via API:
   POST /api/facility/warehouses  { code: "WH-BKK-01", name: "Bangkok DC", lat: 13.7, lng: 100.5, serviceModes: ["Amr", "Manual"] }

2. Trigger RIOT3 map import (Transport.Amr):
   POST /api/transport-amr/maps/import  { riot3MapId: 42, warehouseId: <bkk-id> }
   → verify AmrMap created with FacilityId; AmrStations imported with FacilityId denormalized

3. Create Order via UI:
   - UI shows Warehouse picker → select Bangkok DC
   - Mode=Amr → AmrStation picker appears
   - Submit → verify Item.PickupWarehouseId + PickupStationId saved

4. Dispatch Trip:
   - Verify Trip.PickupWarehouseId + PickupStationId snapshot
   - Verify outbound RIOT3 call uses station VendorRef (resolved through warehouse → AmrMap → AmrStation)

5. Vehicle list page:
   - Verify Vehicle list shows core fields only (no battery)
   - Verify AmrUnit details page shows battery + charging (joined)
```

## Before vs After

### Before — Single Station Entity (Facility module)
```csharp
// Facility.Domain.Entities.Station
public class Station {
    public Guid MapId;
    public Coordinate Coordinate;   // factory-local x/y
    public string? VendorRef;       // RIOT3 ID
    public Dictionary Actions;       // RIOT3 ACT config
}
```

```csharp
// Item references station directly
public Guid? PickupStationId;       // assumes AMR semantics
```

```csharp
// Vehicle has battery + AdapterKey
public class Vehicle {
    public double BatteryLevel;
    public string AdapterKey = "riot3";
    public string VendorVehicleKey;
}
```

### After — Warehouse (Facility) ↔ AmrStation (Transport.Amr)
```csharp
// Facility.Domain.Entities.Warehouse (NEW)
public class Warehouse {
    public LatLng Location;
    public Address Address;
    public int? GeofenceRadiusM;
    public List<TransportMode> ServiceModes;
}

// Transport.Amr.Domain.Entities.AmrStation (RENAMED + MOVED)
public class AmrStation {
    public Guid MapId;
    public Guid FacilityId;          // ← link back to Warehouse
    public Coordinate Coordinate;
    public string? VendorRef;
}
```

```csharp
// Item references both — Warehouse always, Station only for AMR
public Guid PickupWarehouseId;       // required
public Guid? PickupStationId;        // AMR-only
```

```csharp
// Vehicle is core registry
public class Vehicle {
    public string RegistrationCode;
    public VehicleStatus Status;
    // no battery, no adapter key
}

// Transport.Amr.Domain.Entities.AmrUnit (NEW)
public class AmrUnit {
    public Guid VehicleId;            // 1:0..1 with Vehicle
    public string VendorVehicleKey;
    public double BatteryLevel;
    public Guid? ChargingPolicyId;
}
```

## Outcome

- ✓ Facility module mode-agnostic — Warehouse first-class with geofence ready for Manual
- ✓ Vehicle core clean — battery/charging concerns isolated to Transport.Amr
- ✓ Schema ready for Manual mode (Warehouse shared, Station optional)
- ✓ Module boundary enforced — Facility doesn't know about RIOT3
- ✓ Frontend 2-step picker correctly models the hierarchy

## Risks & Mitigation

| Risk | Mitigation |
|---|---|
| Schema reset breaks dev environments | Document seed re-import procedure; provide `scripts/reseed-dev.sh` |
| Missed `using` statements from rename | Build verification + architecture tests |
| EF migration order across modules | Stagger MigrationId timestamps; verify with `dotnet ef migrations list` |
| Frontend StationCombobox usage missed | Grep before merge: `grep -r "StationCombobox" frontend/` |
| RIOT3 sync breaks (Map import) | Stage rollout: import 1 map → smoke test → bulk import |

## Next Phase

→ [Phase 3: Dispatch Plan Abstraction + Trip Extensions](phase-3-dispatch-abstraction.md)
