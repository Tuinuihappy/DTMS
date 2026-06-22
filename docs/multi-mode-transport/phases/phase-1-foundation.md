# Phase 1 — Foundation: Namespace + DI Rewire

- **Sprint**: 1-2
- **Risk**: Low (mechanical rename + additive abstractions)
- **Schema change**: None
- **Frontend impact**: None
- **Depends on**: (none — first phase)

## Goal

Rename existing `VendorAdapter.*` projects to `Transport.Amr.*`, promote `VendorAdapter.Abstractions` to peer `Transport.Abstractions`, และเพิ่ม dispatch abstractions (Strategy + Router) — โดย **ไม่เปลี่ยน behavior ใดๆ**

## Task Checklist

### Step 1: Rename Projects (mechanical)

```bash
# Backup branch
git checkout -b refactor/phase-1-foundation

# Rename project folders
git mv src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Abstractions \
       src/Modules/Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions

git mv src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Riot3 \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr

git mv src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Feeder

git mv src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Simulator \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Simulator

git mv src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Infrastructure \
       src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Infrastructure
```

### Step 2: Update .csproj files (rename inside each .csproj)

ในแต่ละ .csproj ปรับ:
- `<AssemblyName>` → `AMR.DeliveryPlanning.Transport.Amr.*`
- `<RootNamespace>` → `AMR.DeliveryPlanning.Transport.Amr.*`
- `<ProjectReference>` ที่ชี้ไปยัง `VendorAdapter.Abstractions` → ชี้ไป `Transport.Abstractions`

**Pattern (representative):**
- [src/Modules/Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions.csproj](../../../src/Modules/Transport.Abstractions/)
- [src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.csproj](../../../src/Modules/Transport.Amr/)

### Step 3: Update Solution File

แก้ `AMR.DeliveryPlanning.slnx` — replace ทุก path/name ที่ contain `VendorAdapter` → `Transport.Amr` หรือ `Transport.Abstractions`

### Step 4: Bulk Namespace Update

ใช้ IDE (Rider/Visual Studio) **Find and Replace in Solution**:
- `AMR.DeliveryPlanning.VendorAdapter.Abstractions` → `AMR.DeliveryPlanning.Transport.Abstractions`
- `AMR.DeliveryPlanning.VendorAdapter.Riot3` → `AMR.DeliveryPlanning.Transport.Amr`
- `AMR.DeliveryPlanning.VendorAdapter.Feeder` → `AMR.DeliveryPlanning.Transport.Amr.Feeder`
- `AMR.DeliveryPlanning.VendorAdapter.Simulator` → `AMR.DeliveryPlanning.Transport.Amr.Simulator`
- `AMR.DeliveryPlanning.VendorAdapter.Infrastructure` → `AMR.DeliveryPlanning.Transport.Amr.Infrastructure`

**Files affected: ~50** — most are `using` statements. Some are inline references (e.g. `nameof()`).

### Step 5: Add New Abstractions

Create new files in `src/Modules/Transport.Abstractions/AMR.DeliveryPlanning.Transport.Abstractions/Services/`:

**`IDispatchStrategy.cs`**:
```csharp
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;

namespace AMR.DeliveryPlanning.Transport.Abstractions.Services;

public interface IDispatchStrategy
{
    TransportMode Mode { get; }
    Task<DispatchResult> DispatchAsync(Trip trip, CancellationToken ct);
}

public sealed record DispatchResult(
    bool Success,
    string? VendorOrderKey,
    string? Reason);
```

**`IDispatchStrategyRegistry.cs`**:
```csharp
namespace AMR.DeliveryPlanning.Transport.Abstractions.Services;

public interface IDispatchStrategyRegistry
{
    IDispatchStrategy Get(TransportMode mode);
    bool IsRegistered(TransportMode mode);
}
```

**`IVendorOperationsRouter.cs`**:
```csharp
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;

namespace AMR.DeliveryPlanning.Transport.Abstractions.Services;

public interface IVendorOperationsRouter
{
    IVendorEnvelopeOperationService For(Trip trip);
    IVendorRobotOperationService? ForRobot(Trip trip);
}
```

**`IVehiclePositionProvider.cs`**:
```csharp
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Abstractions.Services;

public interface IVehiclePositionProvider
{
    TransportMode Mode { get; }
    IAsyncEnumerable<PositionUpdate> StreamAsync(CancellationToken ct);
}

public sealed record PositionUpdate(
    Guid VehicleId,
    double X,
    double Y,
    double? Theta,
    DateTime ObservedAt,
    double? BatteryLevel);
```

### Step 6: Update `appsettings.json`

```json
{
  "TransportModes": {
    "Amr":    { "Enabled": true,  "Vendor": "Riot3", "Riot3": { /* moved from existing VendorAdapter.Riot3 block */ } },
    "Manual": { "Enabled": false },
    "Fleet":  { "Enabled": false }
  }
}
```

Keep old `VendorAdapter:Riot3:*` config block ไว้ก่อน (เป็น dead config) — ลบใน Phase 2 ถ้า no references

### Step 7: Refactor DI Registration

**Create** `src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/TransportAmrServiceCollectionExtensions.cs`:

```csharp
namespace AMR.DeliveryPlanning.Transport.Amr;

public static class TransportAmrServiceCollectionExtensions
{
    public static IServiceCollection AddTransportAmr(this IServiceCollection services, IConfiguration config)
    {
        var amrConfig = config.GetSection("TransportModes:Amr");
        if (!amrConfig.GetValue<bool>("Enabled")) return services;

        // ย้าย registration จาก ModuleServiceRegistration.cs:296-299 มาที่นี่
        services.AddScoped<IVendorEnvelopeOperationService, Riot3VendorEnvelopeOperationAdapter>();
        services.AddScoped<IVendorRobotOperationService, Riot3VendorRobotOperationAdapter>();
        services.AddScoped<IDispatchStrategy, AmrDispatchStrategy>();  // เพิ่มใหม่

        // Background services (ย้ายมาจาก Program.cs)
        services.AddHostedService<Riot3PositionPollerService>();
        services.AddHostedService<Riot3ReconciliationService>();
        services.AddHostedService<Riot3HealthPollerService>();
        // ... etc.

        return services;
    }
}
```

**Create stubs** สำหรับ Manual + Fleet (ทำให้ composition root อ่านเป็น pattern เดียวกัน):

`src/Modules/Transport.Manual/AMR.DeliveryPlanning.Transport.Manual/TransportManualServiceCollectionExtensions.cs`:
```csharp
public static class TransportManualServiceCollectionExtensions
{
    public static IServiceCollection AddTransportManual(this IServiceCollection services, IConfiguration config)
    {
        var manualConfig = config.GetSection("TransportModes:Manual");
        if (!manualConfig.GetValue<bool>("Enabled")) return services;

        // (Phase 4 จะใส่ implementation จริง)
        throw new InvalidOperationException("Transport.Manual not implemented yet — disable in config");
    }
}
```

(Same pattern for `TransportFleetServiceCollectionExtensions`)

### Step 8: Update Composition Root

**[ModuleServiceRegistration.cs](../../../src/AMR.DeliveryPlanning.Api/Modules/ModuleServiceRegistration.cs)** ลบ inline registrations ที่ line 296-299, แทนด้วย:

```csharp
services.AddTransportAmr(configuration);
services.AddTransportManual(configuration);
services.AddTransportFleet(configuration);
```

### Step 9: Implement First Strategy (Wrap Existing)

**`src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Application/Services/AmrDispatchStrategy.cs`**:

```csharp
public sealed class AmrDispatchStrategy : IDispatchStrategy
{
    private readonly IRiot3CommandService _riot3;

    public AmrDispatchStrategy(IRiot3CommandService riot3) => _riot3 = riot3;

    public TransportMode Mode => TransportMode.Amr;

    public async Task<DispatchResult> DispatchAsync(Trip trip, CancellationToken ct)
    {
        // Phase 1: เรียก existing flow โดยไม่เปลี่ยน behavior
        // (Phase 3 จะ refactor จริง — แต่ใน Phase 1 แค่ wrap)
        var result = await _riot3.SendOrderAsync(/* build request from trip */, ct);
        return new DispatchResult(result.Success, result.VendorOrderKey, result.Reason);
    }
}
```

### Step 10: Implement Router (Pass-through)

**`src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr/Application/Services/VendorOperationsRouter.cs`**:

```csharp
public sealed class VendorOperationsRouter : IVendorOperationsRouter
{
    private readonly Riot3VendorEnvelopeOperationAdapter _riot3Envelope;
    private readonly Riot3VendorRobotOperationAdapter _riot3Robot;

    public VendorOperationsRouter(
        Riot3VendorEnvelopeOperationAdapter riot3Envelope,
        Riot3VendorRobotOperationAdapter riot3Robot)
    {
        _riot3Envelope = riot3Envelope;
        _riot3Robot = riot3Robot;
    }

    public IVendorEnvelopeOperationService For(Trip trip)
    {
        // Phase 1: AMR เท่านั้น — ตรวจสอบและ throw ถ้าเจอ mode อื่น
        if (trip.TransportMode != TransportMode.Amr)
            throw new NotSupportedException($"Mode {trip.TransportMode} not implemented yet");
        return _riot3Envelope;
    }

    public IVendorRobotOperationService? ForRobot(Trip trip)
        => trip.TransportMode == TransportMode.Amr ? _riot3Robot : null;
}
```

**Register**: ใน `AddTransportAmr()` → `services.AddScoped<IVendorOperationsRouter, VendorOperationsRouter>();`

### Step 11: Update Tests Namespace

```bash
git mv tests/Modules/VendorAdapter.UnitTests tests/Modules/Transport.Amr.UnitTests
git mv tests/Modules/VendorAdapter.IntegrationTests tests/Modules/Transport.Amr.IntegrationTests
```

Update test project namespaces + `using` statements (Find & Replace).

### Step 12: Add New Tests

**`tests/Modules/Transport.Amr.UnitTests/VendorOperationsRouterTests.cs`** (NEW):
```csharp
[Fact]
public void For_AmrTrip_ReturnsRiot3Adapter()
{
    var trip = TripBuilder.With(TransportMode.Amr).Build();
    var router = new VendorOperationsRouter(_riot3Envelope, _riot3Robot);
    router.For(trip).Should().Be(_riot3Envelope);
}

[Fact]
public void For_ManualTrip_Throws()
{
    var trip = TripBuilder.With(TransportMode.Manual).Build();
    var router = new VendorOperationsRouter(_riot3Envelope, _riot3Robot);
    Action act = () => router.For(trip);
    act.Should().Throw<NotSupportedException>();
}
```

## Verification

### Build & Test

```bash
# Gate 1: Compile
dotnet build --configuration Release

# Gate 2: Unit tests
dotnet test tests/Modules/ --no-build --logger "console;verbosity=minimal"

# Gate 3: Integration tests
dotnet test tests/Integration/AMR.DeliveryPlanning.IntegrationTests/ --no-build

# Gate 4: Architecture tests
dotnet test tests/ArchitectureTests/ --no-build
```

### Manual Smoke Test

```
1. Start API: dotnet run --project src/AMR.DeliveryPlanning.Api
2. Health check: GET /healthz → 200
3. Create order: POST /api/delivery-orders (TransportMode=Amr default)
4. Trigger dispatch (via planning) → verify outbound RIOT3 call
   (use Simulator: src/Modules/Transport.Amr/AMR.DeliveryPlanning.Transport.Amr.Simulator)
5. POST /api/webhooks/riot3/notify with task FINISHED event
6. Verify Trip transitions: Created → InProgress → Completed
7. Check logs for "AmrDispatchStrategy" + "VendorOperationsRouter" being invoked
```

### Architecture Tests to Add

```csharp
// tests/ArchitectureTests/ModuleBoundaryTests.cs
[Fact]
public void DispatchModule_ShouldNotReferenceTransportAmr()
{
    var dispatch = typeof(Trip).Assembly;
    var amrAssemblyName = "AMR.DeliveryPlanning.Transport.Amr";
    var refs = dispatch.GetReferencedAssemblies().Select(a => a.Name);
    refs.Should().NotContain(amrAssemblyName);
}
```

## Before vs After

### Before
```
src/Modules/
└── VendorAdapter/
    ├── Abstractions/         ← contracts
    ├── Riot3/                ← AMR vendor impl
    ├── Feeder/               ← webhooks
    ├── Simulator/            ← test stub
    └── Infrastructure/       ← factory, outbox
```

```csharp
// ModuleServiceRegistration.cs
services.AddScoped<IVendorEnvelopeOperationService, Riot3VendorEnvelopeOperationAdapter>();
services.AddScoped<IVendorRobotOperationService, Riot3VendorRobotOperationAdapter>();
```

### After
```
src/Modules/
├── Transport.Abstractions/   ← shared contracts (peer)
└── Transport.Amr/            ← AMR mode (peer)
    ├── Transport.Amr/        (Domain/Application/Infrastructure/Presentation)
    ├── Transport.Amr.Feeder/
    ├── Transport.Amr.Simulator/
    └── Transport.Amr.Infrastructure/
```

```csharp
// ModuleServiceRegistration.cs
services.AddTransportAmr(configuration);
services.AddTransportManual(configuration);    // no-op until Phase 4
services.AddTransportFleet(configuration);     // no-op until Phase 5
```

## Outcome

- ✓ Naming reflects domain (Transport modes) not pattern (VendorAdapter)
- ✓ Composition root uniform: `AddTransport*()` per mode
- ✓ Router + Strategy pattern in place — ready for Manual/Fleet
- ✓ Architecture tests enforce boundary (Dispatch can't reference Transport.Amr)
- ✓ **Zero behavior change** — all existing AMR flows work identically
- ✓ Rollback simple: `git revert` (no schema or data change)

## Next Phase

→ [Phase 2: Facility / Vehicle Split](phase-2-facility-vehicle-split.md)
