# Templates

Reusable boilerplate สำหรับ contribute เอกสาร / code ในส่วน multi-mode transport refactor

## ที่มีอยู่

### Documentation
| Template | Purpose | When to use |
|---|---|---|
| [adr-template.md](adr-template.md) | Architecture Decision Record | เมื่อจะตัดสินใจสำคัญที่ใช้ระยะยาว (architecture, library choice, schema design, security) |

### Backend — Domain & Application (.cs)
| Template | Purpose | When to use |
|---|---|---|
| [aggregate-template.cs](aggregate-template.cs) | Domain aggregate root (DDD) | สร้าง entity ใหม่ที่เป็น aggregate root (Trip, Operator, FleetProvider) |
| [command-handler-template.cs](command-handler-template.cs) | Command + Handler pair | สร้าง use case ใหม่ (Pause/Resume/Cancel patterns, CapturePoD, AssignOperator) |
| [consumer-template.cs](consumer-template.cs) | MassTransit IConsumer\<T\> | Cross-module integration event handler (TripCompleted, OrderConfirmed, etc.) |
| [background-service-template.cs](background-service-template.cs) | IHostedService / BackgroundService | Poll, reconcile, watchdog, cleanup, snapshot patterns |

### Backend — Infrastructure (.cs)
| Template | Purpose | When to use |
|---|---|---|
| [repository-template.cs](repository-template.cs) | Repository (Domain interface + Infrastructure impl) | Persistence boundary for aggregate; EF Core + Npgsql retry-aware |
| [migration-template.cs](migration-template.cs) | EF Core migration file | เมื่อจะเพิ่ม / แก้ schema (per [ADR-008](../adr/adr-008-migration-strategy.md)) |
| [migration-designer-template.cs](migration-designer-template.cs) | Migration model snapshot (companion) | คู่กับ migration template เสมอ |

### Frontend (.tsx)
| Template | Purpose | When to use |
|---|---|---|
| [frontend-component-template.tsx](frontend-component-template.tsx) | Next.js + shadcn (base-ui) | Mode-aware UI component with SWR + state + handlers |

### Tests (.cs)
| Template | Purpose | When to use |
|---|---|---|
| [unit-test-template.cs](unit-test-template.cs) | xUnit + NSubstitute + FluentAssertions | Domain aggregate tests + handler tests (mocked deps) |
| [integration-test-template.cs](integration-test-template.cs) | DtmsWebApplicationFactory + Testcontainers | HTTP API + DB + outbox roundtrip; cross-module flow |

## วิธีใช้

### สำหรับ ADR ใหม่

```bash
# 1. ดูเลข ADR ล่าสุด
ls docs/multi-mode-transport/adr/

# 2. Copy template + ตั้งชื่อใหม่
cp docs/multi-mode-transport/templates/adr-template.md \
   docs/multi-mode-transport/adr/adr-011-{your-slug}.md

# 3. แก้ตาม instructions ภายในไฟล์ (ลบ <!-- INSTRUCTIONS --> ออกตอน commit)

# 4. Update README index:
#    - File tree
#    - Summary table (1 row)
#    - Reading order (ถ้า role-relevant)

# 5. Add backlinks ใน ADRs ที่เกี่ยวข้อง (their "Related ADRs" sections)
```

### สำหรับ Aggregate / Domain Entity ใหม่

```bash
# Example: creating Operator aggregate in Phase 4
cp docs/multi-mode-transport/templates/aggregate-template.cs \
   src/Modules/Transport.Manual/.../Domain/Entities/Operator.cs

# Then:
# 1. Replace all {Placeholder} tokens
# 2. Reference existing examples in same module for style consistency
# 3. Write domain events (e.g., OperatorCreatedDomainEvent.cs)
# 4. Write repository interface (e.g., IOperatorRepository.cs in Domain/Repositories/)
# 5. Add unit tests using unit-test-template.cs
```

### สำหรับ Command + Handler ใหม่

```bash
# Example: AssignOperatorToTrip command in Phase 4
mkdir -p src/Modules/Transport.Manual/.../Application/Commands/AssignOperatorToTrip
cp docs/multi-mode-transport/templates/command-handler-template.cs /tmp/temp.cs

# Split into 2 files:
#   AssignOperatorToTripCommand.cs       (record + ICommand)
#   AssignOperatorToTripCommandHandler.cs (handler + dependencies)

# Replace placeholders + reference existing handler in same module
```

### สำหรับ Tests

```bash
# Unit test (fast, mocked):
cp docs/multi-mode-transport/templates/unit-test-template.cs \
   tests/Modules/Transport.Manual.UnitTests/OperatorTests.cs

# Integration test (real DB + HTTP):
cp docs/multi-mode-transport/templates/integration-test-template.cs \
   tests/Integration/DTMS.IntegrationTests/OperatorAssignmentIntegrationTests.cs

# Verify:
dotnet test tests/Modules/Transport.Manual.UnitTests/
dotnet test tests/Integration/DTMS.IntegrationTests/ --filter "FullyQualifiedName~OperatorAssignment"
```

### สำหรับ Consumer (cross-module event handler)

```bash
# Example: ManualTripStalled event from SLA watchdog
cp docs/multi-mode-transport/templates/consumer-template.cs \
   src/Modules/Dispatch/.../Application/Consumers/ManualTripStalledConsumer.cs

# Then:
# 1. Replace placeholders + reference existing consumer in same module
# 2. Register in MassTransit setup (Program.cs OR module extension)
# 3. Write integration test (use ITestHarness or full DtmsWebApplicationFactory)
```

### สำหรับ Background Service

```bash
# Example: SLA watchdog in Phase 4
cp docs/multi-mode-transport/templates/background-service-template.cs \
   src/Modules/Transport.Manual/.../Application/BackgroundServices/ManualTripSlaWatchdog.cs

# Then:
# 1. Replace placeholders
# 2. Create Options class + register with Configure<>
# 3. Add config section in appsettings.json
# 4. Register via services.AddHostedService<>()
# 5. Verify: dotnet run + check logs for "[ServiceName] started"
```

### สำหรับ Repository

```bash
# Example: OperatorRepository in Phase 4
# File 1: Domain interface
cp docs/multi-mode-transport/templates/repository-template.cs /tmp/repo.cs
# Split into:
#   src/Modules/Transport.Manual/.../Domain/Repositories/IOperatorRepository.cs
#   src/Modules/Transport.Manual/.../Infrastructure/Repositories/OperatorRepository.cs

# Then:
# 1. Replace {Entity} → Operator, {Module} → Transport.Manual everywhere
# 2. Register: services.AddScoped<IOperatorRepository, OperatorRepository>()
# 3. Write fake repo in tests/.../Fakes/ for handler unit tests
```

### สำหรับ Frontend Component

```bash
# Example: Operator assignment UI in Phase 4
cp docs/multi-mode-transport/templates/frontend-component-template.tsx \
   frontend/components/transport/manual/operator-assignment-card.tsx

# Then:
# 1. Replace placeholders
# 2. Add API client in frontend/lib/api/manual-operator.ts
# 3. Verify: cd frontend && npm run typecheck && npm run lint && npm run build
```

### สำหรับ Migration ใหม่

```bash
# 1. หาเลข MigrationId — ดู ADR-008 เรื่อง timestamp convention
#    ดู migrations ที่มีอยู่:
find src -name "*.cs" -path "*/Migrations/*" -not -name "*.Designer.cs" | sort | tail -10

# 2. Copy templates:
cp docs/multi-mode-transport/templates/migration-template.cs \
   src/Modules/{Module}/.../Infrastructure/Migrations/{Id}_{Name}.cs

cp docs/multi-mode-transport/templates/migration-designer-template.cs \
   src/Modules/{Module}/.../Infrastructure/Migrations/{Id}_{Name}.Designer.cs

# 3. แก้ทั้ง 2 ไฟล์ (placeholders + actual logic)

# 4. Update {YourDbContext}ModelSnapshot.cs ให้ตรงกับ model state หลัง migration นี้

# 5. Verify:
docker compose down -v && docker compose up -d postgres
dotnet build --configuration Release
dotnet run --project src/DTMS.Api
# ตรวจ __EFMigrationsHistory ว่ามี MigrationId นี้ + ไม่มี duplicate
```

## Template Patterns Used in DTMS (Quick Reference)

| Pattern | Where it appears | Template |
|---|---|---|
| Aggregate root with state machine | Trip, DeliveryOrder, Job | [aggregate-template.cs](aggregate-template.cs) |
| Command + Handler with vendor reconciliation | PauseTrip, ResumeTrip, CancelTrip | [command-handler-template.cs](command-handler-template.cs) |
| Cross-module event consumer | TripCompletedConsumer, OrderCancelledCascadeConsumer | [consumer-template.cs](consumer-template.cs) |
| Polling background service | PlanningReconciliation, Riot3PositionPoller, OutboxProcessor | [background-service-template.cs](background-service-template.cs) |
| Repository with EF Core | TripRepository, DeliveryOrderRepository | [repository-template.cs](repository-template.cs) |
| Mode-aware React component | TripActionBar, TripDetailDrawer | [frontend-component-template.tsx](frontend-component-template.tsx) |
| Domain unit tests with builders | Dispatch.UnitTests, DeliveryOrder.UnitTests | [unit-test-template.cs](unit-test-template.cs) |
| HTTP + DB + outbox integration | Riot3WebhookTests, EndToEndPipelineTests | [integration-test-template.cs](integration-test-template.cs) |
| Manual EF migrations | All Infrastructure/Migrations/ folders | [migration-template.cs](migration-template.cs) |
| ADR (immutable decision record) | All `adr/` files | [adr-template.md](adr-template.md) |

## Style Conventions Embedded in Templates

These conventions are consistent across all DTMS modules — templates encode them:

- **Aggregate fields**: all private setters; mutation via methods only
- **Factory methods**: static, validate inputs, raise domain events
- **State transitions**: idempotent where possible (return early instead of throw)
- **Repository pattern**: interface in Domain, impl in Infrastructure
- **Commands**: records with positional params + `ICommand` / `ICommand<T>`
- **Handler results**: `Result` / `Result<T>` — never throw for business failures
- **Test naming**: `{Method}_{Scenario}_{ExpectedBehavior}` (verb-first method names)
- **Test doubles**: Stub/Fake prefix, sit in `Fakes/` folder
- **Domain events**: `{Entity}{Action}DomainEvent` (past tense action)
- **Integration events**: `{Entity}{Action}IntegrationEventV{N}` (versioned)

## เพิ่ม Template ใหม่

ถ้ามี pattern อื่นที่ใช้ซ้ำๆ — สร้าง template ใหม่ใน folder นี้แล้ว update README นี้

แนวทาง pattern อื่นที่อาจ valuable ในอนาคต:
- **Domain event template** — `{Entity}{Action}DomainEvent` records + event handlers
- **Integration event template** — versioned cross-module events with outbox publish pattern
- **Minimal API endpoint template** — `MapGroup` + endpoint filters + DTO + result mapping
- **Validation pipeline behavior template** — FluentValidation in MediatR/MassTransit pipeline
- **Projector template** — IdempotentProjector pattern (TripFactsProjector, TripItemsProjector)
- **SignalR hub method template** — live update hub (TripHub, RobotPositionHub)
- **Frontend page template** — Next.js App Router page with server component + client islands

ข้อแนะนำ:
- Self-contained — ไม่ต้องอ่าน doc อื่นเพิ่ม
- Inline comments อธิบาย placeholder + when to delete
- Reference real examples ใน codebase (กับ file path)
- ระบุ verification steps

## References

- [ADR-008: Migration Strategy](../adr/adr-008-migration-strategy.md) — สาเหตุที่ต้อง manual migrations + conventions
- Existing migrations as reference:
  - [Dispatch migrations](../../../src/Modules/Dispatch/DTMS.Dispatch.Infrastructure/Migrations/)
  - [Facility migrations](../../../src/Modules/Facility/DTMS.Facility.Infrastructure/Migrations/)
- Existing ADRs as reference: see [adr/ folder](../adr/)
- ADR origin (Michael Nygard): https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions
