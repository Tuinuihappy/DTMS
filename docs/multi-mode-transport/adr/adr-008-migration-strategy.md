# ADR-008: Database Migration Strategy

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [ADR-001](adr-001-multi-mode-transport-split.md), [ADR-002](adr-002-facility-station-hierarchy.md), [ADR-003](adr-003-trip-extension-tables.md)

## Context

Multi-mode refactor มี migration ที่ซับซ้อนกว่าทั่วไป:
- 5 phases × หลาย modules per phase = ~15-20 migrations รวม
- 7 DbContexts ทำงาน against database เดียว (ผ่าน schemas)
- Per [memory `feedback_migration_manual`](../../memory/feedback_migration_manual.md): **EF migrations ต้องเขียนเอง** — `dotnet-ef` CLI ไม่ compatible กับ .NET 10 preview
- Per [memory `project_shared_migration_history`](../../memory/project_shared_migration_history.md): ทุก DbContext share `public.__EFMigrationsHistory` — MigrationId ต้อง unique cross-module ไม่งั้นบาง migration จะ skip เงียบๆ
- Pre-launch state — schema reset acceptable, แต่ dev environments ของทีมห้ามพังบ่อย

ปัญหาที่ต้องตัดสิน:
1. Migration file authoring — manual ทั้งหมด หรือ generate + edit?
2. MigrationId timestamping convention — ใครชนกันใครชนะ?
3. Cross-module migration order — Phase 2 มี migrations จาก 5 modules, run order สำคัญ
4. Rollback strategy — schema reset (per ADR scope) vs Down() migrations
5. Dev environment refresh — ลบ DB ทุกครั้ง vs incremental
6. CI verification — auto-apply migrations ตรวจสภาพ schema?
7. Production migration (post-launch) — zero-downtime vs maintenance window

## Decision

ใช้ **5 conventions** เป็นมาตรฐาน:

### Convention 1: Manual Authoring (Always)

- ห้ามใช้ `dotnet ef migrations add` (broken on .NET 10 preview)
- เขียน `*_Description.cs` files ด้วยมือ + `*_Description.Designer.cs` snapshot
- ใช้ existing migrations เป็น template: [src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Migrations/](../../../src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Migrations/)
- Update `*DbContextModelSnapshot.cs` หลังเพิ่ม migration

### Convention 2: MigrationId Timestamp Spacing

Cross-module migrations in same phase use **non-overlapping seconds**:

```
Phase 2 migrations (all in this slot):
20260628000000_PromoteWarehouseAggregate          (Facility)
20260628000001_CreateAmrSchema                    (Transport.Amr)
20260628000002_StripVehicleAmrFields              (Vehicle)
20260628000003_AddWarehouseFkToItem               (DeliveryOrder)
20260628000004_AddWarehouseFkToTrip               (Dispatch)
20260628000005_AddWarehouseFkToOrderTemplate      (Planning)
```

**Rules:**
- 1 phase = 1 minute window (60 slots — plenty)
- Number ตาม dependency order (parent-first: Warehouse → AmrStation → references)
- Phase boundaries: leave 1 day gap between phases (`20260628*` → `20260712*`)

**Pattern in code:**
```csharp
[DbContext(typeof(FacilityDbContext))]
[Migration("20260628000000_PromoteWarehouseAggregate")]
partial class PromoteWarehouseAggregate { ... }
```

### Convention 3: Migration Order Within Phase

**Dependency-aware ordering** — declared in phase doc + enforced via timestamp:

```
Phase 2 Order:
1. Facility: Create Warehouse table
   ↓ (Warehouse must exist before AmrMap.FacilityId FK)
2. Transport.Amr: Create AmrSchema + AmrMap + AmrStation
   ↓
3. Vehicle: Strip battery/charging from Vehicle
   ↓ (parallel ok)
4. DeliveryOrder: Add WarehouseId to Item
5. Dispatch: Add WarehouseId to Trip
6. Planning: Add WarehouseId to OrderTemplate
```

Document order in phase doc's `### Step N: EF Migrations` section. CI runs in order via startup `Migrate()` (each DbContext migrates its own pending list).

### Convention 4: Idempotency Guards

Every migration includes existence checks (re-runnable):

```csharp
public partial class CreateAmrSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent schema create
        migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS transport_amr;");

        // Idempotent table create
        migrationBuilder.Sql(@"
            CREATE TABLE IF NOT EXISTS transport_amr.amr_maps (
                id UUID PRIMARY KEY,
                ...
            );
        ");

        // Or use EF Core fluent API which is already idempotent via __EFMigrationsHistory check
        migrationBuilder.CreateTable(
            name: "amr_maps",
            schema: "transport_amr",
            columns: table => new { ... });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS transport_amr.amr_maps;");
        // Don't drop schema (other migrations may still need it)
    }
}
```

### Convention 5: Per-DbContext Migration Application

At startup, **each DbContext migrates independently**:

```csharp
// in Program.cs or hosted startup service
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DispatchDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<FacilityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TransportAmrDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TransportManualDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TransportFleetDbContext>().Database.MigrateAsync();
    // ... etc
}
```

**Order matters:** Migrate parent FK targets first (Facility before Transport.Amr). Encode in startup code; don't rely on Dictionary iteration order.

## Migration File Template

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.{Module}.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class {DescriptiveName} : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PHASE: {Phase number}
            // PURPOSE: {1-line description}
            // DEPENDS ON: {Previous migration name(s) — or "none"}
            // REVERSIBLE: {Yes/No — explain if No}

            migrationBuilder.CreateTable(...);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Document if Down() is destructive (data loss) or partial
            migrationBuilder.DropTable(...);
        }
    }
}
```

Companion `*.Designer.cs` file MUST be updated — it's the EF model snapshot at that point in time. Edit manually:

```csharp
[DbContext(typeof(TransportAmrDbContext))]
[Migration("20260628000001_CreateAmrSchema")]
partial class CreateAmrSchema
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        // ... full model snapshot at this migration's state
    }
}
```

**Also update** `*DbContextModelSnapshot.cs` (the current snapshot) — keep in sync with latest migration's Designer

## Phase-by-Phase Migration Plan

### Phase 1 (Foundation)

**No schema changes.** Only namespace renames in code. Existing `_VendorAdapter*` migrations stay (no rename needed — Migration class names are independent of project name).

**Verification:** `dotnet run` startup applies 0 new migrations.

### Phase 2 (Facility / Vehicle Split)

```
20260628000000_PromoteWarehouseAggregate                (Facility)
20260628000001_CreateAmrSchema                          (Transport.Amr)
20260628000002_MoveStationsAndMapsToAmrSchema           (Transport.Amr — copy data)
20260628000003_DropStationsAndMapsFromFacilitySchema    (Facility)
20260628000004_RenameFleetToVehicleSchema               (Vehicle)
20260628000005_StripBatteryAndChargingFromVehicle       (Vehicle)
20260628000006_CreateAmrUnitTable                       (Transport.Amr)
20260628000007_AddWarehouseFkToItem                     (DeliveryOrder)
20260628000008_AddWarehouseFkToTrip                     (Dispatch)
20260628000009_AddWarehouseFkToOrderTemplate            (Planning)
```

**Order rationale:**
- (0) Create Warehouse first — others FK to it
- (1-3) AMR schema migration — preserve data via copy
- (4-6) Vehicle split — strip then re-create on AMR side
- (7-9) Cross-cutting FKs (parallel possible after Warehouse exists)

### Phase 3 (Dispatch Abstraction + Trip Extensions)

```
20260712000000_CreateAmrTripExtensions                  (Transport.Amr)
20260712000001_BackfillVendorFieldsToAmrTripExtensions  (Transport.Amr)
20260712000002_StripVendorFieldsFromTrip                (Dispatch)
20260712000003_DropMissionsFromOrderTemplate            (Planning)
20260712000004_CreateAmrDispatchPlanTemplates           (Transport.Amr)
20260712000005_MoveActionTemplatesToAmrSchema           (Transport.Amr)
```

### Phase 4 (Transport.Manual)

```
20260726000000_CreateTransportManualSchema              (Transport.Manual)
20260726000001_CreateOperatorsTable                     (Transport.Manual)
20260726000002_CreateOperatorShiftsTable                (Transport.Manual)
20260726000003_CreateOperatorCertificationsTable        (Transport.Manual)
20260726000004_CreateOperatorDevicesTable               (Transport.Manual)
20260726000005_CreateManualTripExtensionsTable          (Transport.Manual)
20260726000006_CreateRefreshTokensTable                 (Transport.Manual)
20260726000007_CreateAuthEventsTable                    (Transport.Manual or shared audit)
```

### Phase 5 (Transport.Fleet)

```
20260809000000_CreateTransportFleetSchema               (Transport.Fleet)
20260809000001_CreateFleetProvidersTable                (Transport.Fleet)
20260809000002_CreateFleetContractsTable                (Transport.Fleet)
20260809000003_CreateWaybillsTable                      (Transport.Fleet)
20260809000004_CreateFleetTripExtensionsTable           (Transport.Fleet)
```

## Rollback Strategy

### Pre-launch (current state — Phase 1-5)

**Strategy: Schema reset acceptable.** ทำงาน against dev/staging only:

```bash
# Full reset (developer machine)
docker compose down -v
docker compose up -d postgres rabbitmq redis
dotnet run --project src/AMR.DeliveryPlanning.Api  # auto-applies all migrations
```

`Down()` migrations เขียนไว้ใน file แต่ไม่ใช้จริง — เก็บไว้เผื่อ post-launch หรือ debugging

### Post-launch (Future — out of this ADR scope)

จะต้องเขียน [ADR-XXX Post-launch Migration Strategy] separately ครอบคลุม:
- Blue-green deployment
- Backwards-compatible schema changes (add nullable first → backfill → make required)
- Feature flag for schema-dependent features
- Down() migrations tested

## Dev Workflow

### Adding a New Migration

```bash
# 1. Identify next MigrationId (look at last migration in that DbContext)
# 2. Create file: src/Modules/{Module}/.../Infrastructure/Migrations/{Id}_{Name}.cs
# 3. Edit Up() and Down()
# 4. Update {Id}_{Name}.Designer.cs (model snapshot at this point)
# 5. Update {DbContextName}ModelSnapshot.cs (current model)
# 6. Run: dotnet run (auto-applies + verifies)
# 7. Run: dotnet test (integration tests use migrated schema)
```

### Squashing Migrations (Pre-launch only)

ถ้า phase migration เริ่ม noisy (> 5 migrations per phase), squash before merging PR:

```bash
# 1. Manually merge migrations into single file
# 2. Update timestamps (use single timestamp for the phase)
# 3. Delete old migration files
# 4. Reset dev DB + verify
```

Document squash decision in phase PR

### Discovering Migrations

Without dotnet-ef CLI, use find:

```bash
find src -name "*.cs" -path "*/Migrations/*" -not -name "*.Designer.cs" | sort
```

## CI Migration Smoke Test

Add to [.github/workflows/ci.yml](../../../.github/workflows/ci.yml):

```yaml
- name: Migration smoke test
  run: |
    # Spin up empty Postgres
    docker run -d --name pg-smoke -p 5433:5432 -e POSTGRES_PASSWORD=postgres postgres:16

    # Wait for ready
    timeout 30 bash -c 'until docker exec pg-smoke pg_isready; do sleep 1; done'

    # Apply all migrations via app startup
    export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"
    timeout 60 dotnet run --project src/AMR.DeliveryPlanning.Api --no-build &
    APP_PID=$!
    sleep 30
    kill $APP_PID || true

    # Verify expected tables exist
    docker exec pg-smoke psql -U postgres -c "\dt facility.*"
    docker exec pg-smoke psql -U postgres -c "\dt transport_amr.*"
    docker exec pg-smoke psql -U postgres -c "\dt transport_manual.*"
    docker exec pg-smoke psql -U postgres -c "\dt transport_fleet.*"

    # Verify no pending migrations
    docker exec pg-smoke psql -U postgres -c "SELECT migration_id, COUNT(*) FROM public.\"__EFMigrationsHistory\" GROUP BY migration_id HAVING COUNT(*) > 1;"
    # ↑ if any duplicates: timestamps collided → fail
```

## Alternatives Considered

### Alternative A: dotnet ef migrations add (auto-generate)

**Pros:** Standard EF workflow, less manual work
**Cons:**
- Per memory: broken on .NET 10 preview (we're stuck with this)
- Auto-generated diffs sometimes wrong (especially with owned entities, complex value objects)

**Rejected because:** tooling unavailable in our stack

### Alternative B: Per-DbContext __EFMigrationsHistory (sharded)

แต่ละ DbContext มี history table ของตัวเอง (e.g., `dispatch.__EFMigrationsHistory`)

**Pros:**
- ไม่มี cross-module timestamp conflict
- Module ๆ ขึ้น/ลง independent

**Cons:**
- Per memory `project_shared_migration_history`: ปัจจุบันใช้ shared `public.__EFMigrationsHistory` — change ต้องการ EF Core config + data migration
- ไม่มี benefit ที่ชัด — timestamps strategy ใช้ได้ดี
- Backup/restore ซับซ้อนขึ้น

**Rejected:** ต้อง refactor existing setup เปล่าๆ; current scheme works

### Alternative C: Flyway / DbUp / Liquibase (third-party migration tools)

**Pros:**
- More mature than EF migrations
- SQL-first (more control)
- No .NET 10 compatibility issues

**Cons:**
- Adds another tool to learn + maintain
- Loses EF model snapshot benefits
- Team experience: ต่ำ
- Existing code already uses EF migrations

**Rejected:** สู้ EF migration workflow ที่ทำงานอยู่ไม่ได้

### Alternative D: Auto-merge migrations into single file at PR

**Pros:** No proliferation
**Cons:**
- Diff review ยาก (giant file)
- Loses incremental history
- Rollback granularity เสีย

**Rejected:** Per-feature migrations dev-friendlier

## Consequences

### Positive

- ✓ Migrations consistent คาดเดาได้ — convention เคร่งครัด
- ✓ Idempotent guards = re-runnable for debugging
- ✓ Per-DbContext migration = module ownership shines through
- ✓ CI smoke test catches timestamp collisions before they hit dev DBs
- ✓ Phase-based timestamps map to plan documents

### Negative

- ✗ Manual authoring + Designer.cs sync = error-prone (mitigated by template + smoke test)
- ✗ Adding migration requires more steps than `dotnet ef` would
- ✗ Cross-module dependency order = developer must understand FK chain
- ✗ MigrationId collision: silent skip in EF — relies on CI smoke check

### Neutral

- Pre-launch flexibility: schema reset OK = lower migration safety burden until launch
- Post-launch migration strategy = separate future ADR

## Edge Cases & Failure Modes

### Edge Case 1: Two PRs Add Same MigrationId Timestamp

Scenario: Dev A adds `20260628000003_X`, Dev B (parallel branch) adds `20260628000003_Y`

**Handling:**
- Merge conflict on `*ModelSnapshot.cs` — git catches
- If conflict missed: CI smoke test detects duplicate ID
- Resolution: re-timestamp Dev B's migration (`...000005_...`) + update snapshot

### Edge Case 2: Migration Half-Applied

Scenario: App crashes mid-migration

**Handling:**
- EF wraps each migration in transaction by default
- Partial migration → rolled back automatically
- `__EFMigrationsHistory` not updated → re-runs on next startup
- Non-transactional DDL (e.g., CREATE INDEX CONCURRENTLY): explicit save points or manual cleanup

### Edge Case 3: Migration Adds Required Column to Table with Data

Scenario: Phase 2 adds `Item.PickupWarehouseId NOT NULL` to existing items

**Handling (pre-launch — DB reset):**
- Reset acceptable; no backfill needed
- Migration uses `NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'` or similar placeholder if data exists in dev DB

**Handling (post-launch — future):**
- Multi-step migration:
  1. Add `NULL`able column
  2. Backfill from existing reference (PickupLocationCode → resolve to WarehouseId)
  3. Set `NOT NULL` constraint after backfill complete

### Edge Case 4: Cross-Module FK Created Before Target Table Exists

Scenario: Transport.Amr migration creates `amr_maps.facility_id → facility.warehouses(id)` but Facility migration not yet applied

**Handling:**
- Startup migration order in `Program.cs` ensures Facility migrates first
- Within phase: timestamp order enforces (Facility migration earlier timestamp)
- Defensive: check `IF NOT EXISTS` on FK creation

### Edge Case 5: Schema Rename / Move (e.g., facility.stations → transport_amr.amr_stations)

Scenario: Phase 2 moves stations to AMR schema with rename

**Handling (preserve data):**
```sql
-- Method 1: Move + rename in single statement
ALTER TABLE facility.stations SET SCHEMA transport_amr;
ALTER TABLE transport_amr.stations RENAME TO amr_stations;

-- Method 2 (if columns also change): CREATE new + INSERT-SELECT + DROP old
CREATE TABLE transport_amr.amr_stations (...);
INSERT INTO transport_amr.amr_stations (...) SELECT ... FROM facility.stations;
DROP TABLE facility.stations;
```

Use Method 2 when column schema changes simultaneously.

## Acceptance Criteria

- [ ] All Phase 2-5 migrations follow timestamp convention (no collisions)
- [ ] Each phase doc includes migration file list with order rationale
- [ ] CI migration smoke test added + passing
- [ ] Migration template file checked in: `docs/multi-mode-transport/templates/migration-template.cs`
- [ ] No duplicate MigrationId in `public.__EFMigrationsHistory` after full apply
- [ ] Squash + reset workflow documented for dev environments

## Related ADRs

- [ADR-001](adr-001-multi-mode-transport-split.md) — Module split (drives schema organization)
- [ADR-002](adr-002-facility-station-hierarchy.md) — Schema reorg (most invasive migration)
- [ADR-003](adr-003-trip-extension-tables.md) — Trip extension migration sequence

## References

- EF Core Migrations: https://learn.microsoft.com/ef/core/managing-schemas/migrations/
- [Memory: feedback_migration_manual](../../memory/feedback_migration_manual.md)
- [Memory: project_shared_migration_history](../../memory/project_shared_migration_history.md)
- Existing migrations: [src/Modules/Dispatch/.../Migrations/](../../../src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Migrations/)
