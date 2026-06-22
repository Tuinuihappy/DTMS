# Phase 3 — Dispatch Plan Abstraction + Trip Extensions

- **Sprint**: 5-6
- **Risk**: High (touches Planning + Dispatch core)
- **Schema change**: Yes (Trip core slim down + extension tables)
- **Frontend impact**: Medium (Trip detail drawer + retry panel)
- **Depends on**: [Phase 1](phase-1-foundation.md), [Phase 2](phase-2-facility-vehicle-split.md)

## Goal

ทำให้ Dispatch module **transport-agnostic จริง**:
1. ย้าย OrderTemplate.Missions + ActionTemplate ที่เป็น RIOT3 concept → Transport.Amr
2. แยก `AmrTripExtension` ออกจาก Trip core (per ADR-003)
3. Implement `IDispatchStrategy` + `IVendorOperationsRouter` ที่มี logic จริง (Phase 1 wrap-only)
4. Refactor Position polling → `IVehiclePositionProvider`

## Task Checklist

### Step 1: Define DispatchPlan Hierarchy

**`src/Modules/Transport.Abstractions/.../Models/DispatchPlan.cs`** (NEW):

```csharp
namespace AMR.DeliveryPlanning.Transport.Abstractions.Models;

public abstract class DispatchPlan
{
    public Guid TripId { get; init; }
    public Guid OrderId { get; init; }
    public Guid PickupWarehouseId { get; init; }
    public Guid DropWarehouseId { get; init; }
    public abstract TransportMode Mode { get; }
}
```

**`src/Modules/Transport.Amr/.../Domain/AmrDispatchPlan.cs`** (NEW):

```csharp
public sealed class AmrDispatchPlan : DispatchPlan
{
    public override TransportMode Mode => TransportMode.Amr;
    public Guid PickupAmrStationId { get; init; }
    public Guid DropAmrStationId { get; init; }
    public IReadOnlyList<AmrMission> Missions { get; init; }
    public AppointVehicleHint? AppointHint { get; init; }
    public string StructureType { get; init; } = "sequence";
}

public sealed record AmrMission(...);
public sealed record AppointVehicleHint(...);
```

(`ManualDispatchPlan` + `FleetDispatchPlan` ใส่ stub class — implementation จริงใน Phase 4/5)

### Step 2: Move OrderTemplate Missions to Transport.Amr

**Current**: [OrderTemplate.cs](../../../src/Modules/Planning/AMR.DeliveryPlanning.Planning.Domain/Entities/OrderTemplate.cs) มี `Missions[]` ตาม RIOT3 spec — ต้องแยก

**Split**:

`src/Modules/Planning/.../Domain/Entities/OrderTemplate.cs` (slim):
```csharp
public class OrderTemplate
{
    public Guid Id { get; }
    public string Name { get; }
    public Guid PickupWarehouseId { get; }
    public Guid DropWarehouseId { get; }
    public CargoSpec? Cargo { get; }
    public TransportMode TargetMode { get; }   // discriminator
    public Guid? AmrPlanTemplateId { get; }    // FK to AmrDispatchPlanTemplate
    // REMOVED: Missions, AppointVehicleHint, StructureType
}
```

`src/Modules/Transport.Amr/.../Domain/Entities/AmrDispatchPlanTemplate.cs` (NEW):
```csharp
public class AmrDispatchPlanTemplate
{
    public Guid Id { get; }
    public Guid OrderTemplateId { get; }       // FK back
    public IReadOnlyList<AmrMissionTemplate> Missions { get; }
    public AppointVehicleHint? AppointHint { get; }
    public string StructureType { get; }
}
```

**Move** `ActionTemplate.cs` entirely → Transport.Amr (it's RIOT3-specific 100%):
```bash
git mv src/Modules/Planning/AMR.DeliveryPlanning.Planning.Domain/Entities/ActionTemplate.cs \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Domain/Entities/ActionTemplate.cs
```

### Step 3: Refactor Trip Aggregate (per ADR-003)

**Strip vendor fields from Trip** — [Trip.cs](../../../src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Entities/Trip.cs):

```csharp
public class Trip
{
    public Guid Id { get; }
    public Guid OrderId { get; }
    public TransportMode TransportMode { get; }   // ← discriminator
    public TripStatus Status { get; private set; }
    public Guid PickupWarehouseId { get; }
    public Guid DropWarehouseId { get; }
    public Guid? PickupStationId { get; }          // AMR-only
    public Guid? DropStationId { get; }            // AMR-only
    public Guid? VehicleId { get; }
    public DateTime CreatedAt { get; }
    public DateTime? StartedAt { get; }
    public DateTime? CompletedAt { get; }
    public string? FailureReason { get; }

    // REMOVED: VendorOrderKey, VendorVehicleKey, VendorVehicleName, VendorPauseSource

    public void MarkInProgress(DateTime at) { ... }   // ← renamed from MarkVendorStarted
    public void MarkPaused() { ... }
    public void MarkResumed() { ... }
    public void MarkCompleted(DateTime at) { ... }
    public void MarkFailed(string reason) { ... }
    public void MarkCancelled() { ... }
}
```

### Step 4: Create AmrTripExtension

**`src/Modules/Transport.Amr/.../Domain/Entities/AmrTripExtension.cs`** (NEW):

```csharp
public class AmrTripExtension
{
    public Guid TripId { get; }                  // PK + FK to dispatch.trips
    public string? VendorOrderKey { get; private set; }
    public string? VendorVehicleKey { get; private set; }
    public string? VendorVehicleName { get; private set; }
    public VendorPauseSource? VendorPauseSource { get; private set; }
    public string? VendorRequestSnapshot { get; private set; }  // JSON
    public DateTime CreatedAt { get; }
    public DateTime? UpdatedAt { get; private set; }

    public void RecordVendorOrder(string orderKey, string requestSnapshot) { ... }
    public void RecordVendorVehicle(string vehicleKey, string? vehicleName) { ... }
    public void SetPauseSource(VendorPauseSource source) { ... }
}
```

**Repository**:
```csharp
// src/Modules/Transport.Amr/.../Application/Services/IAmrTripExtensionRepository.cs
public interface IAmrTripExtensionRepository
{
    Task<AmrTripExtension?> GetByTripIdAsync(Guid tripId, CancellationToken ct);
    Task AddAsync(AmrTripExtension ext, CancellationToken ct);
    Task UpdateAsync(AmrTripExtension ext, CancellationToken ct);
}
```

### Step 5: Implement IDispatchStrategy with Real Logic

**`src/Modules/Transport.Amr/.../Application/Services/AmrDispatchStrategy.cs`** (refactor Phase 1 stub):

```csharp
public sealed class AmrDispatchStrategy : IDispatchStrategy
{
    private readonly IRiot3CommandService _riot3;
    private readonly IAmrTripExtensionRepository _amrTripExt;
    private readonly IAmrDispatchPlanBuilder _planBuilder;

    public TransportMode Mode => TransportMode.Amr;

    public async Task<DispatchResult> DispatchAsync(Trip trip, CancellationToken ct)
    {
        // 1. Build AmrDispatchPlan
        var plan = await _planBuilder.BuildAsync(trip, ct);

        // 2. POST to RIOT3
        var result = await _riot3.SendOrderAsync(plan, ct);
        if (!result.Success)
            return new DispatchResult(false, null, result.ErrorMessage);

        // 3. Create AmrTripExtension
        var ext = new AmrTripExtension(trip.Id, result.VendorOrderKey, plan.ToJson());
        await _amrTripExt.AddAsync(ext, ct);

        return new DispatchResult(true, result.VendorOrderKey, null);
    }
}
```

### Step 6: Refactor Pause/Resume/Cancel Handlers

**[PauseTripCommandHandler.cs](../../../src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Commands/PauseTrip/PauseTripCommandHandler.cs)** — ใช้ router:

```csharp
public async Task Handle(PauseTripCommand cmd, CancellationToken ct)
{
    var trip = await _trips.GetByIdAsync(cmd.TripId, ct);
    if (trip.Status != TripStatus.InProgress)
        throw new InvalidOperationException("Trip not in progress");

    var vendorOps = _router.For(trip);              // ← resolves per mode
    var outcome = await vendorOps.PauseAsync(trip.Id, ct);

    if (outcome == VendorOperationOutcome.Accepted) {
        trip.MarkPaused();
        await _trips.UpdateAsync(trip, ct);
    } else if (outcome == VendorOperationOutcome.NoVendorRecord) {
        trip.MarkFailed("Vendor has no record of this trip");
        await _trips.UpdateAsync(trip, ct);
    }
}
```

**Riot3VendorEnvelopeOperationAdapter** — read AmrTripExtension:

```csharp
public async Task<VendorOperationOutcome> PauseAsync(Guid tripId, CancellationToken ct)
{
    var ext = await _amrTripExt.GetByTripIdAsync(tripId, ct);
    if (ext?.VendorOrderKey is null) return VendorOperationOutcome.NoVendorRecord;

    var riot3Outcome = await _riot3.PauseEnvelopeAsync(ext.VendorOrderKey, ct);
    return MapOutcome(riot3Outcome);
}
```

### Step 7: Refactor Riot3PositionPollerService → IVehiclePositionProvider

**`src/Modules/Transport.Amr/.../Application/Services/Riot3PositionProvider.cs`** (NEW):

```csharp
public sealed class Riot3PositionProvider : IVehiclePositionProvider
{
    private readonly IRiot3FacilityClient _riot3;

    public TransportMode Mode => TransportMode.Amr;

    public async IAsyncEnumerable<PositionUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            var vehicles = await _riot3.GetVehiclesAsync(ct);
            foreach (var v in vehicles) {
                yield return new PositionUpdate(...);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
```

**`Riot3PositionPollerService`** กลายเป็น thin orchestrator ที่ subscribe ทุก `IVehiclePositionProvider`:

```csharp
public sealed class VehiclePositionPollerService : BackgroundService
{
    private readonly IEnumerable<IVehiclePositionProvider> _providers;
    private readonly IRobotPositionStore _store;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var tasks = _providers.Select(p => ConsumeAsync(p, ct));
        await Task.WhenAll(tasks);
    }

    private async Task ConsumeAsync(IVehiclePositionProvider p, CancellationToken ct)
    {
        await foreach (var update in p.StreamAsync(ct)) {
            await _store.UpdateAsync(update);
        }
    }
}
```

### Step 8: Update Migrations

```sql
-- 20260712000000_StripVendorFieldsFromTrip.cs (Dispatch.Infrastructure)
ALTER TABLE dispatch.trips
    DROP COLUMN vendor_order_key,
    DROP COLUMN vendor_vehicle_key,
    DROP COLUMN vendor_vehicle_name,
    DROP COLUMN vendor_pause_source;

-- 20260712000001_CreateAmrTripExtensions.cs (Transport.Amr.Infrastructure)
CREATE TABLE transport_amr.amr_trip_extensions (
    trip_id UUID PRIMARY KEY REFERENCES dispatch.trips(id) ON DELETE CASCADE,
    vendor_order_key VARCHAR(200),
    vendor_vehicle_key VARCHAR(200),
    vendor_vehicle_name VARCHAR(200),
    vendor_pause_source VARCHAR(50),
    vendor_request_snapshot JSONB,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ
);

CREATE INDEX ix_amr_trip_extensions_vendor_order_key
    ON transport_amr.amr_trip_extensions(vendor_order_key)
    WHERE vendor_order_key IS NOT NULL;

-- 20260712000002_DropOrderTemplateMissions.cs (Planning.Infrastructure)
ALTER TABLE planning.order_templates
    DROP COLUMN missions,
    DROP COLUMN appoint_vehicle_hint,
    DROP COLUMN structure_type;

ALTER TABLE planning.order_templates
    ADD COLUMN target_mode VARCHAR(50) NOT NULL DEFAULT 'Amr',
    ADD COLUMN amr_plan_template_id UUID;

-- 20260712000003_CreateAmrDispatchPlanTemplates.cs (Transport.Amr.Infrastructure)
CREATE TABLE transport_amr.amr_dispatch_plan_templates (
    id UUID PRIMARY KEY,
    order_template_id UUID NOT NULL,
    missions JSONB NOT NULL,
    appoint_vehicle_hint JSONB,
    structure_type VARCHAR(50) NOT NULL DEFAULT 'sequence'
);

-- 20260712000004_MoveActionTemplates.cs
ALTER TABLE planning.action_templates SET SCHEMA transport_amr;
```

### Step 9: Frontend — Decouple from Vendor Fields

[trip-detail-drawer.tsx](../../../frontend/components/dispatch/trip-detail-drawer.tsx) — ลบการอ้าง `trip.vendorOrderKey` ตรง; ใช้ separate API:

```typescript
// frontend/lib/api/transport-amr.ts (NEW)
export async function getAmrTripExtension(tripId: string): Promise<AmrTripExtension | null>;

// Trip detail drawer
{trip.mode === 'Amr' && (
  <AmrTripExtensionPanel tripId={trip.id} />   // lazy load
)}
```

## Verification

### Build & Test

```bash
# Migrations
docker compose restart postgres
dotnet run --project src/AMR.DeliveryPlanning.Api  # auto-applies migrations

# Test gates
dotnet build --configuration Release
dotnet test --no-build --logger "console;verbosity=minimal"
cd frontend && npm run typecheck && npm run lint && npm run build
```

### Critical Existing Tests (must stay green)

```bash
# Trip state machine + handlers (346 LOC)
dotnet test tests/Modules/Dispatch.UnitTests/UnitTest1.cs

# RIOT3 wire shape
dotnet test tests/Modules/Transport.Amr.UnitTests/

# Full webhook flow
dotnet test tests/Integration/AMR.DeliveryPlanning.IntegrationTests/Riot3WebhookTests.cs
```

### NEW Tests

```csharp
// IDispatchStrategy contract
[Fact]
public async Task AmrDispatchStrategy_Returns_VendorOrderKey_OnSuccess() { ... }

[Fact]
public async Task AmrDispatchStrategy_CreatesAmrTripExtension() { ... }

// Router routing
[Theory]
[InlineData(TransportMode.Amr, typeof(Riot3VendorEnvelopeOperationAdapter))]
public void Router_For_ReturnsAdapterMatchingMode(TransportMode mode, Type expected) { ... }

// Pause handler agnostic to mode
[Fact]
public async Task PauseTripHandler_DelegatesToRouter() { ... }

// Architecture tests
[Fact]
public void DispatchApplication_ShouldNotReferenceRiot3Specifics() {
    typeof(PauseTripCommandHandler).Assembly
        .GetTypes()
        .SelectMany(t => t.GetMethods())
        .SelectMany(m => m.GetParameters())
        .Select(p => p.ParameterType.Namespace)
        .Should().NotContain(ns => ns?.Contains("Riot3") == true);
}
```

### Manual Smoke Test

```
Full RIOT3 dispatch lifecycle ทำงานเหมือนเดิม 100%:

1. Create Order (TransportMode=Amr) → dispatched
2. Trip created → AmrDispatchStrategy invoked → AmrTripExtension row created
3. RIOT3 webhook TASK_PROCESSING → Trip.MarkInProgress + AmrTripExtension.RecordVendorVehicle
4. POST /trips/{id}/pause → Router resolves Amr → Riot3 adapter reads AmrTripExtension → POST RIOT3
5. RIOT3 webhook TASK_HELD → AmrTripExtension.SetPauseSource(Held)
6. POST /trips/{id}/resume → reads ext.VendorPauseSource → CONTINUE_FROM_HELD
7. RIOT3 webhook TASK_FINISHED → Trip.MarkCompleted
8. Verify projections (TripFactsRow, TripStatusHistoryRow) updated correctly
```

## Before vs After

### Before — Trip Fat
```csharp
public class Trip {
    // 25+ fields including:
    public string? VendorOrderKey;
    public string? VendorVehicleKey;
    public string? VendorVehicleName;
    public VendorPauseSource? VendorPauseSource;
}

// Handlers read directly:
trip.MarkVendorStarted(vehicleKey, vehicleName);
trip.VendorOrderKey  // direct access
```

### After — Trip Slim + Extensions
```csharp
public class Trip {
    // ~15 fields (mode-agnostic core)
    public TransportMode TransportMode;  // discriminator
}

public class AmrTripExtension {
    public Guid TripId;
    public string? VendorOrderKey;
    // ... AMR-specific
}

// Handlers delegate via router:
var ops = _router.For(trip);
await ops.PauseAsync(trip.Id, ct);

// Adapter reads extension:
var ext = await _amrTripExt.GetByTripIdAsync(tripId);
await _riot3.PauseEnvelopeAsync(ext.VendorOrderKey);
```

## Outcome

- ✓ Dispatch module compiles without Transport.Amr reference
- ✓ Trip aggregate clean — mode-agnostic core
- ✓ AMR-specific data in AmrTripExtension (per ADR-003)
- ✓ AmrDispatchStrategy is first real impl of IDispatchStrategy
- ✓ Position telemetry abstracted — ready for Manual GPS + Fleet provider
- ✓ Pattern locked in: เพิ่ม Manual/Fleet = new Strategy + new Extension + register in IVendorOperationsRouter

## Risks & Mitigation

| Risk | Mitigation |
|---|---|
| Pause/Resume break (vendor key lookup) | Integration test webhook → pause → resume → complete flow |
| OrderTemplate migration data loss | Pre-launch (no production data); seed re-import script |
| Performance regression (extra query for ext) | Use Include() or eager join; benchmark Trip queries |
| Projection breakage | Run all existing projection tests; manual smoke |
| Architecture rule too strict | Add to allowed exceptions: Outbox + IntegrationEvents (cross-module) |

## Next Phase

→ [Phase 4: Implement Transport.Manual](phase-4-transport-manual.md)
