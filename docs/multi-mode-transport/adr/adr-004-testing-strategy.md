# ADR-004: Testing Strategy for Multi-Mode Refactor

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [ADR-001](adr-001-multi-mode-transport-split.md), all phase docs

## Context

Multi-mode refactor (5 phases, ~10 sprints) มี risk สูง — กระทบ Trip aggregate, projections, dispatcher console, AMR webhooks ที่มีอยู่. User selected **strict gate** (per plan): ทุก phase ต้อง green tests + manual smoke ก่อนเข้า phase ถัดไป

ของที่มี (ตรวจจาก codebase):
- **17 test projects**, ~67 test files, ~13,858 LOC
- xUnit + NSubstitute + FluentAssertions (no other mocking libraries)
- Testcontainers (Postgres 16 + RabbitMQ 3.13 + Redis 7) สำหรับ integration tests
- `DtmsWebApplicationFactory` ใช้ใน [tests/Integration/](../../../tests/Integration/) e2e harness
- ArchitectureTests (ArchUnitNET) ใน [tests/ArchitectureTests/](../../../tests/ArchitectureTests/)
- CI runs `dotnet test` แยก unit + integration tiers; **ไม่มี** frontend test runner, **ไม่มี** migration smoke test, **ไม่มี** mutation testing

ปัญหาที่ต้องตัดสิน:
1. แต่ละ phase ต้อง test อะไรบ้าง? (test pyramid balance)
2. รักษา existing AMR tests ขณะ refactor ยังไง? (regression prevention)
3. Manual mode มี mobile API + push notification — test ยังไง?
4. Architecture boundary enforcement — ใครเป็นเจ้าของ?
5. Migration safety — pre-launch reset OK แต่ dev environment ต้องไม่พัง

## Decision

ใช้ **4-tier strategy** บังคับใช้ผ่าน CI + phase gates:

### Tier 1: Unit Tests (FAST, MANY)
- **Scope**: Domain aggregates (invariants), command handlers (with mocked deps), pure functions
- **Tools**: xUnit + NSubstitute + FluentAssertions
- **Run frequency**: ทุก commit (CI + pre-commit hook)
- **Speed target**: < 5 seconds total
- **Coverage target**: ≥ 80% สำหรับ new modules (Transport.Manual, Transport.Fleet)
- **Existing baseline**: รักษา 100% ของ test ที่ pass อยู่แล้ว (Dispatch.UnitTests/UnitTest1.cs 346 LOC, Transport.Amr.UnitTests 255 LOC)

### Tier 2: Integration Tests (REAL DB + MQ)
- **Scope**: Full HTTP request → DB → integration event → projection flow
- **Tools**: `DtmsWebApplicationFactory` + Testcontainers (Postgres/RabbitMQ/Redis)
- **Run frequency**: PR + main branch
- **Speed target**: < 5 minutes per project
- **Required tests**:
  - Per mode: end-to-end happy path (dispatch → state transitions → complete)
  - Per mode: failure path (vendor reject, geofence violation, provider timeout)
  - Cross-cutting: webhook signature verification, idempotency, retry behavior
- **Existing baseline**: `Riot3WebhookTests` lifecycle test ต้อง pass หลังทุก phase

### Tier 3: Architecture Tests (RULES, ENFORCE BOUNDARIES)
- **Scope**: Module dependency rules, naming conventions, layer enforcement
- **Tools**: ArchUnitNET (already in `tests/ArchitectureTests/`)
- **Run frequency**: PR + main
- **Required rules** (add in each phase):
  - Phase 1: `VendorAdapter.*` ห้ามมีอยู่ (rename complete)
  - Phase 2: `Facility.*` ห้าม reference `Transport.Amr.*`
  - Phase 3: `Dispatch.Application` ห้าม reference `Transport.*` (must go through Abstractions)
  - Phase 4: `Transport.Manual.*` ห้าม reference `Transport.Amr.*`
  - Phase 5: `Transport.Fleet.*` ห้าม reference `Transport.{Amr,Manual}.*`
  - All phases: extension tables ต้องอยู่ใน schema ของ module ตัวเอง (no cross-schema FK ยกเว้นกลับมาที่ Dispatch.Trips)

### Tier 4: Manual Smoke (PER-PHASE GATE)
- **Scope**: User journey end-to-end ผ่าน real API
- **Tools**: Postman/Insomnia collection + checklist
- **Run frequency**: ก่อน merge แต่ละ phase PR
- **Phase-specific scripts** อยู่ในแต่ละ phase doc (`## Verification > Manual Smoke Test`)

### Build Verification
- **Tools**: `dotnet build --configuration Release` (warnings ห้ามเพิ่ม)
- **Tools**: `npm run typecheck && npm run lint && npm run build` (frontend)
- **Required**: ทุก phase ที่กระทบ frontend (Phase 2, 4, 5)

## Test Categories per Phase

### Phase 1 (Foundation — Namespace Rename)
| Tier | Required |
|---|---|
| Unit | Existing 67 files pass + add 3 router contract tests |
| Integration | Existing Riot3WebhookTests pass (proves rename didn't break) |
| Architecture | Add: `VendorAdapter.*` not exists |
| Smoke | Full AMR dispatch lifecycle |

### Phase 2 (Facility/Vehicle Split)
| Tier | Required |
|---|---|
| Unit | Warehouse aggregate, AmrUnit, Item validation (mode-aware) |
| Integration | Migration applies; existing webhook resolves AmrStation; Vehicle list with AmrUnit join |
| Architecture | Facility ↛ Transport.Amr; Vehicle ↛ Transport.Amr |
| Smoke | Order create with 2-step picker; AMR dispatch still works |

### Phase 3 (Dispatch Abstraction)
| Tier | Required |
|---|---|
| Unit | IDispatchStrategy contract, IVendorOperationsRouter routing, AmrDispatchStrategy persistence |
| Integration | Full RIOT3 flow ผ่าน abstractions; AmrTripExtension created on dispatch |
| Architecture | Dispatch.Application ↛ Transport.* (only Abstractions) |
| Smoke | Pause/Resume/Cancel ผ่าน router (verify same RIOT3 calls) |

### Phase 4 (Transport.Manual)
| Tier | Required |
|---|---|
| Unit | Operator invariants, assignment policy, geofence check, SLA calculation |
| Integration | Mobile API happy path; geofence rejection; offline sync replay; webhook→push flow |
| Architecture | Transport.Manual ↛ Transport.Amr; new module follows layered DDD |
| Smoke | Full Manual journey (ack → pickup → drop → complete); SLA breach test |

### Phase 5 (Transport.Fleet)
| Tier | Required |
|---|---|
| Unit | Provider selector, Kerry client wire shape, webhook signature verification |
| Integration | Mock Kerry server (WireMock); reconciliation poll → status update |
| Architecture | Transport.Fleet ↛ Transport.{Amr,Manual}; provider impls behind interface |
| Smoke | 3-mode parallel test (1 AMR + 1 Manual + 1 Fleet order at same time) |

## Test Patterns to Adopt

### Pattern 1: Builder for Domain Aggregates

ตอนนี้ไม่มี — เพิ่มเฉพาะที่จำเป็น:

```csharp
public sealed class TripBuilder
{
    private TransportMode _mode = TransportMode.Amr;
    private TripStatus _status = TripStatus.Created;
    private Guid _pickupWarehouseId = Guid.NewGuid();
    // ...

    public TripBuilder With(TransportMode mode) { _mode = mode; return this; }
    public TripBuilder InStatus(TripStatus status) { _status = status; return this; }
    public TripBuilder PickupAt(Guid warehouseId) { _pickupWarehouseId = warehouseId; return this; }
    public Trip Build() => Trip.CreateForEnvelope(..., _mode, ...);
}

// Usage
var trip = new TripBuilder().With(TransportMode.Manual).InStatus(TripStatus.InProgress).Build();
```

### Pattern 2: Theory tests for State Machine

```csharp
[Theory]
[InlineData(TripStatus.Created, "Cancel", TripStatus.Cancelled, true)]
[InlineData(TripStatus.Created, "Pause", TripStatus.Created, false)]   // illegal
[InlineData(TripStatus.InProgress, "Pause", TripStatus.Paused, true)]
[InlineData(TripStatus.Completed, "Cancel", TripStatus.Completed, false)]
public void Trip_StateTransition(TripStatus from, string action, TripStatus expectedTo, bool shouldSucceed) { ... }
```

### Pattern 3: Mock Vendor Adapter

```csharp
// in tests/Modules/Transport.Manual.UnitTests/Fakes/
public sealed class FakePushNotificationGateway : IPushNotificationGateway
{
    public List<(Guid operatorId, INotification notification)> Sent = new();
    public Task SendAsync(Guid operatorId, INotification n, CancellationToken ct)
    {
        Sent.Add((operatorId, n));
        return Task.CompletedTask;
    }
}
```

### Pattern 4: WireMock for External Providers (Phase 5)

```csharp
public class KerryProviderIntegrationTests : IClassFixture<WireMockFixture>
{
    [Fact]
    public async Task CreateShipment_PostsCorrectWireShape() {
        _wireMock.Given(Request.Create().WithPath("/api/v1/shipments").UsingPost())
                 .RespondWith(Response.Create().WithBodyAsJson(new {
                     waybillNumber = "WB001", shipmentId = "S001"
                 }));
        // ... act + assert
    }
}
```

## Alternatives Considered

### Alternative A: Skip integration tests for new modules (use only unit)

**Pros:** Fast, ไม่ต้อง spin Testcontainers
**Cons:**
- Mobile API geofence + push notification ไม่สามารถ test ใน unit ได้ครบ
- Webhook signature verification ต้อง test end-to-end
- Provider integration (Kerry) ต้องการ wire-shape verification

**Rejected because:** mode-specific failure modes (geofence, provider quirks, webhook delivery) ต้องการ real HTTP

### Alternative B: Add E2E Playwright tests สำหรับ frontend

**Pros:** Cover full user journey รวม UI
**Cons:**
- Frontend ยังไม่มี test runner (Jest/Vitest)
- Setup เพิ่ม ~3-5 sprints
- High maintenance cost (UI churn)

**Rejected for now:** Manual smoke per phase พอ — revisit หลัง Phase 5 ถ้า bug rate สูง

### Alternative C: Mutation testing (Stryker.NET)

**Pros:** วัด test quality (ไม่ใช่แค่ coverage)
**Cons:**
- Slow (minutes per module)
- Noise high — refactor-killing
- Team experience ต่ำ

**Rejected for now:** เพิ่ม later เมื่อ test suite stable

### Alternative D: Snapshot testing สำหรับ projections

**Pros:** Catches projection schema changes
**Cons:**
- Snapshot churn เยอะตอน refactor (Phase 2-3 จะ noisy)
- Existing tests cover projection correctness แล้ว

**Rejected:** Existing TripFactsProjectorTests + TripItemsProjectorTests พอ

## Consequences

### Positive

- ✓ Strict gate ป้องกัน regression — 5 phases ที่ใหญ่ของแบบนี้ ห้ามมี "ทำเสร็จแล้ว break ของเก่า"
- ✓ ArchitectureTests บังคับ module boundary — ไม่ depend on developer discipline
- ✓ Builder pattern เร่ง test writing สำหรับ new aggregates (Operator, Waybill)
- ✓ WireMock pattern reusable สำหรับ Fleet providers ในอนาคต (Flash, J&T)

### Negative

- ✗ Phase 4/5 ต้อง spin Testcontainers จำนวนมาก — CI time เพิ่ม ~5-10 นาที
- ✗ Mobile API integration tests ต้อง simulate device fingerprint + push token — boilerplate เพิ่ม
- ✗ ArchitectureTests false positive risk — ต้อง maintain allowlist (Outbox + IntegrationEvents cross-module)
- ✗ Pre-commit hook อาจช้าถ้า unit suite ใหญ่ — limit ไว้ < 5s

### Neutral

- Existing 67 test files ส่วนใหญ่ไม่ต้องแก้ — namespace rename auto handled
- Frontend testing deferred — manual smoke + typecheck + lint พอจน Phase 5
- CI cost: Postgres + RabbitMQ + Redis containers x 17 test projects — already optimized via parallelism

## Implementation Notes

### CI Configuration Updates Needed

[.github/workflows/ci.yml](../../../.github/workflows/ci.yml) — เพิ่ม:

```yaml
# Add frontend gate (Phase 2 onwards)
- name: Frontend typecheck + lint + build
  if: contains(github.event.pull_request.labels.*.name, 'frontend-touched')
  working-directory: frontend
  run: |
    npm ci
    npm run typecheck
    npm run lint
    npm run build

# Add migration smoke (Phase 2 onwards)
- name: EF Migration smoke
  run: |
    dotnet ef database update --project src/Modules/Facility/.../Infrastructure
    dotnet ef database update --project src/Modules/Transport.Amr/.../Infrastructure
    # ... etc.

# Run architecture tests prominently
- name: Architecture rules
  run: dotnet test tests/ArchitectureTests/ --logger "console;verbosity=normal"
```

### Test Project Conventions

- New test project = `tests/Modules/{Module}.{Tier}Tests/` (matches existing convention)
- Test class naming: `{Subject}Tests.cs` (e.g., `OperatorTests.cs`, `ManualDispatchStrategyTests.cs`)
- Test method naming: `{Method}_{Scenario}_{ExpectedBehavior}` (e.g., `AssignTrip_WhenAlreadyHasTrip_Throws`)

### Allowed Exceptions to Architecture Rules

```csharp
// in tests/ArchitectureTests/ModuleBoundaryTests.cs
private static readonly string[] AllowedCrossModuleReferences = {
    "AMR.DeliveryPlanning.Dispatch.IntegrationEvents",  // events cross module by design
    "AMR.DeliveryPlanning.VendorAdapter.Outbox",         // shared outbox infrastructure
    "AMR.DeliveryPlanning.Transport.Abstractions"        // shared contracts
};
```

## Acceptance Criteria

- [ ] Every phase PR ผ่าน 4-tier gate ก่อน merge
- [ ] New test coverage ≥ 80% สำหรับ new modules
- [ ] Existing test coverage ไม่ลด (snapshot before each phase)
- [ ] ArchitectureTests block PR ที่ละเมิด module boundary
- [ ] CI total time < 15 minutes (split into parallel jobs)
- [ ] Manual smoke checklists เก็บใน [phase docs](../phases/)

## References

- [Phase 1 Test Checkpoints](../phases/phase-1-foundation.md#verification)
- [Phase 4 Mobile API Integration Tests](../phases/phase-4-transport-manual.md#new-tests)
- ArchUnitNET docs: https://github.com/TNG/ArchUnitNET
- Testcontainers .NET: https://testcontainers.com/modules/dotnet
- NSubstitute idioms: https://nsubstitute.github.io/help/getting-started/
