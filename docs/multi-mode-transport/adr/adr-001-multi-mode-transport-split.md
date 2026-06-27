# ADR-001: Multi-Mode Transport Module Split

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Supersedes**: N/A
- **Related**: [ADR-002 Facility/Station Hierarchy](adr-002-facility-station-hierarchy.md), [ADR-003 Trip Extension Tables](adr-003-trip-extension-tables.md)

## Context

DTMS ตั้งใจรองรับหลาย transport modes ตั้งแต่ต้น (มี `TransportMode { Amr, Manual, Fleet }` enum) แต่ **infrastructure ฝัง AMR/RIOT3 assumptions ไว้ลึก**:

- `VendorAdapter/Riot3` คือ "AMR module" โดยพฤตินัย แต่ตั้งชื่อตาม integration pattern (vendor adapter) ไม่ใช่ domain (transport mode)
- DI registration ที่ [ModuleServiceRegistration.cs:296-299](../../../src/DTMS.Api/Modules/ModuleServiceRegistration.cs#L296-L299) ผูก `IVendorEnvelopeOperationService → Riot3VendorEnvelopeOperationAdapter` ตายตัว
- Command handlers (Pause/Resume/Cancel) เรียก vendor adapter ตรง — ไม่มี mode routing
- 8 background services (Position poller, Reconciliation, Health, Map sync, etc.) registered ที่ composition root โดยไม่มี feature flag

Business push:
- Manual mode: operator + mobile app — operator แทน robot
- Fleet mode: 3PL provider (Kerry, Flash) — outsource last-mile

ถ้าเพิ่ม mode ใหม่บน foundation ปัจจุบัน → จะกลายเป็น `if (mode == Manual)` กระจายทั่วระบบ → ลบยาก, test ยาก, scale ไม่ได้

## Decision

แยก code ออกเป็น **peer modules ตาม transport mode** ภายใต้ namespace `Transport.*`:

```
src/Modules/
└── Transport modes (peer modules)
    ├── Transport.Abstractions/   ← shared contracts (interfaces + DTOs only)
    ├── Transport.Amr/            ← RIOT3 (rename จาก VendorAdapter.Riot3 + Feeder + Simulator)
    ├── Transport.Manual/         ← operator + mobile (NEW)
    └── Transport.Fleet/          ← 3PL providers (NEW)
```

แต่ละ Transport module:
- มี Domain / Application / Infrastructure / Presentation layers (DDD standard)
- Implement contracts จาก `Transport.Abstractions`
- Register ตนเองผ่าน `services.AddTransportXxx(config)` extension method
- Toggle ผ่าน config flag `TransportModes:{Mode}:Enabled`

### Core Abstractions (in `Transport.Abstractions`)

```csharp
// dispatch strategy resolved per mode
public interface IDispatchStrategy {
    TransportMode Mode { get; }
    Task<DispatchResult> DispatchAsync(Trip trip, CancellationToken ct);
}

// router resolves vendor ops adapter per trip's mode
public interface IVendorOperationsRouter {
    IVendorEnvelopeOperationService For(Trip trip);
}

// telemetry abstraction (each mode has its own source)
public interface IVehiclePositionProvider {
    TransportMode Mode { get; }
    IAsyncEnumerable<PositionUpdate> StreamAsync(CancellationToken ct);
}

// existing contracts moved up to Abstractions
public interface IVendorEnvelopeOperationService { ... }   // pause/resume/cancel
public interface IVendorRobotOperationService { ... }      // pass
```

### Dispatch Flow Change

**Before:**
```csharp
// CreateEnvelopeTripCommandHandler
await _riot3CommandService.SendOrderAsync(...);  // ผูกกับ RIOT3 ตายตัว
```

**After:**
```csharp
// CreateEnvelopeTripCommandHandler
var strategy = _dispatchStrategies.Get(order.TransportMode);
await strategy.DispatchAsync(trip, ct);
// AmrDispatchStrategy   → Riot3CommandService.SendOrderAsync
// ManualDispatchStrategy → push notification + create ManualTripExtension
// FleetDispatchStrategy  → IFleetProviderClient.CreateShipment
```

## Alternatives Considered

### Alternative A: Keep `VendorAdapter/Riot3` structure, add `VendorAdapter/Manual` + `VendorAdapter/Fleet`

**Pros:** ไม่ต้อง rename, low immediate churn
**Cons:**
- Manual operator ไม่ใช่ "vendor" — semantic blur
- `VendorAdapter` name ผูก pattern ไม่ใช่ domain
- ไม่บังคับให้คิดเรื่อง module boundary

**Rejected because:** ปัญหาคือ naming + structure ผูก pattern เกินไป — แก้ตอนนี้ก่อนมี module ที่ 2 ถูกกว่า

### Alternative B: Inheritance — `ManualTrip : Trip`, `AmrTrip : Trip`

**Pros:** OOP straightforward, mode-specific behavior อยู่ที่ subclass
**Cons:**
- ทำลาย Trip FSM ที่ใช้ร่วมกัน
- EF inheritance (TPH/TPT/TPC) ทำให้ projections + BI query ซับซ้อน
- มี Phase P1, P5.2, P5.3 projection investment ที่จะเสียถ้า Trip split

**Rejected because:** Trip lifecycle (Created → InProgress → Paused → Completed/Failed/Cancelled) เหมือนกันทุก mode — แค่ "ใครเป็นคนทำ" ต่างกัน → Strategy pattern เหมาะกว่า inheritance

### Alternative C: Big-bang rewrite of Dispatch + spawn new system per mode

**Pros:** Clean slate per mode
**Cons:**
- ทิ้ง Trip FSM + projections ที่มีอยู่
- 3 dispatcher consoles แทน 1 — UX แย่
- BI report cross-mode ทำไม่ได้

**Rejected because:** Trip aggregate + projections + dispatcher console เป็น asset — refactor ดีกว่ารื้อ

## Consequences

### Positive

- ✓ เพิ่ม mode ใหม่ = สร้าง project ใหม่ + impl interfaces — ไม่ต้องแก้ Dispatch
- ✓ Module boundary ชัดเจน enforced ผ่าน ArchitectureTests
- ✓ Composition root อ่านง่าย: `AddTransportAmr() + AddTransportManual() + AddTransportFleet()`
- ✓ Test แต่ละ mode แยกได้ — ไม่มี cross-mode coupling
- ✓ Feature flag ปิด mode ได้ runtime (รัน DTMS ที่ deploy เฉพาะ AMR หรือเฉพาะ Fleet ได้)

### Negative

- ✗ Phase 1 ต้อง rename ~50 files (mechanical แต่ tedious)
- ✗ ต้องการ ArchitectureTests strict — เพิ่ม CI time
- ✗ Developer onboarding ต้องเข้าใจ Strategy + Router pattern ก่อน
- ✗ Indirection layer เพิ่ม 1 ระดับ (router resolve → adapter call) — แต่ negligible performance

### Neutral

- Existing AMR flow ทำงานเหมือนเดิม 100% หลัง Phase 1-3
- Migration risk จัดการได้ด้วย pre-launch reset (per ADR scope)
- Frontend `transport/*` folder structure ขนานกับ backend modules — natural mapping

## Implementation Notes

### DI Registration Pattern (canonical)

```csharp
// ModuleServiceRegistration.cs (composition root)
public static IServiceCollection AddDtmsModules(this IServiceCollection services, IConfiguration config)
{
    // Shared kernel (always on)
    services.AddDeliveryOrderModule(config);
    services.AddPlanningModule(config);
    services.AddFacilityModule(config);
    services.AddDispatchModule(config);
    services.AddVehicleModule(config);
    services.AddOmsAdapterModule(config);

    // Abstractions (always on — interfaces only)
    services.AddTransportAbstractions();

    // Transport modes (conditional per [ADR-006](adr-006-transport-mode-feature-flag.md))
    services.AddTransportAmr(config);
    services.AddTransportManual(config);
    services.AddTransportFleet(config);

    return services;
}
```

### Strategy Registry Implementation

```csharp
// in Transport.Abstractions
public sealed class DispatchStrategyRegistry : IDispatchStrategyRegistry
{
    private readonly Dictionary<TransportMode, IDispatchStrategy> _strategies;

    public DispatchStrategyRegistry(IEnumerable<IDispatchStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.Mode);
    }

    public IDispatchStrategy Get(TransportMode mode) => _strategies.TryGetValue(mode, out var s)
        ? s
        : throw new TransportModeNotEnabledException(mode);

    public bool IsRegistered(TransportMode mode) => _strategies.ContainsKey(mode);
}
```

Each `AddTransportXxx()` registers its `IDispatchStrategy` via `services.AddScoped<IDispatchStrategy, AmrDispatchStrategy>()` — registry auto-discovers via DI

### Validation: Order/Mode Compatibility

Order creation must validate that the requested mode is **registered**:

```csharp
// in DeliveryOrder.Application — CreateOrderCommandHandler
public async Task<Result<Guid>> Handle(CreateOrderCommand cmd, CancellationToken ct)
{
    if (!_strategyRegistry.IsRegistered(cmd.TransportMode))
        return Result.Fail($"Transport mode {cmd.TransportMode} is not enabled in this deployment");

    // ... create order
}
```

→ Order in `Manual` mode rejected at intake if Manual mode disabled (no orphan trips)

## Edge Cases & Failure Modes

### Edge Case 1: Mode Disabled Mid-Flight

Scenario: Manual mode disabled while in-flight Manual trips exist

**Behavior:**
- Existing trips continue (ManualDispatchStrategy still resolved from registry until DI rebuilt at restart)
- After restart: in-flight Manual trips become "stranded" (no router resolution)
- Mitigation: pre-restart drain procedure — `IsRegistered(mode)` check + admin alert before restart

**Decision:** Document as operational concern, not code change. Enable/disable is deploy-level decision.

### Edge Case 2: Order Created with Future-Mode

Scenario: Schema has `TransportMode.Drone` but Drone module not yet implemented

**Behavior:**
- Order creation: `IsRegistered(Drone)` returns false → reject at intake
- Existing orders with Drone (pre-rollback): query returns mode value, but dispatch fails

**Decision:** Schema enum + IsRegistered check is sufficient guard.

### Edge Case 3: Router Resolution Failure

Scenario: Trip exists with mode `X`, but no adapter registered for `X`

**Behavior:**
- `IVendorOperationsRouter.For(trip)` throws `TransportModeNotEnabledException`
- Pause/Resume/Cancel handlers catch → return `422 Unprocessable Entity` with clear error
- Background reconciliation services log + skip (don't crash)

```csharp
public IVendorEnvelopeOperationService For(Trip trip)
{
    if (!_registry.IsRegistered(trip.TransportMode))
        throw new TransportModeNotEnabledException(
            $"Cannot operate on trip {trip.Id}: mode {trip.TransportMode} not enabled. " +
            "Check appsettings.json TransportModes:{Mode}:Enabled");
    return _adapters[trip.TransportMode];
}
```

### Edge Case 4: Cross-Mode Trip Conversion

Scenario: Manual operator unavailable → can we convert Manual trip to Fleet?

**Decision: NO** — Trip is mode-immutable after creation.

**Why:**
- `Trip.TransportMode` is snapshot from order at create time
- Each mode has different extension table — conversion requires data migration
- BI reports + projections assume mode immutability
- Use cancel + re-create instead: Cancel(Manual trip) → CreateOrder(Fleet mode) → Dispatch

**Documented constraint:** Trip aggregate enforces `TransportMode` setter is `private set` with no public mutator

## Performance Considerations

- Router lookup: `Dictionary<TransportMode, IDispatchStrategy>` = O(1)
- DI resolution: scoped lifetime, no per-request overhead beyond first resolve
- No reflection, no scanning at runtime
- Strategy invocation: 1 extra virtual call vs. direct (negligible)

Benchmark expectation (Phase 3 target): router overhead < 10 μs per dispatch — confirmed via micro-benchmark in test suite.

## Related ADRs

- [ADR-002](adr-002-facility-station-hierarchy.md) — Facility split (separate axis of refactor)
- [ADR-003](adr-003-trip-extension-tables.md) — Trip extension tables (complement to mode split)
- [ADR-004](adr-004-testing-strategy.md) — How module boundaries are enforced in tests
- [ADR-006](adr-006-transport-mode-feature-flag.md) — How modes are enabled/disabled per deployment

## Implementation Roadmap

- [Phase 1: Foundation](../phases/phase-1-foundation.md) — rename + abstractions
- [Phase 3: Dispatch Abstraction](../phases/phase-3-dispatch-abstraction.md) — IDispatchStrategy implementation
- [Phase 4: Transport.Manual](../phases/phase-4-transport-manual.md) — first non-AMR mode
- [Phase 5: Transport.Fleet](../phases/phase-5-transport-fleet.md) — second non-AMR mode

## References

- Strategy Pattern (Gang of Four)
- Dependency Inversion Principle (Robert C. Martin)
- Hexagonal Architecture (Alistair Cockburn)
- Existing DTMS abstractions: [IVendorEnvelopeOperationService.cs](../../../src/Modules/Dispatch/DTMS.Dispatch.Application/Services/IVendorEnvelopeOperationService.cs)
