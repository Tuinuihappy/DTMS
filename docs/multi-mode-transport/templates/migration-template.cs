// =============================================================================
// EF MIGRATION FILE TEMPLATE
// =============================================================================
//
// Filename convention:
//   {YYYYMMDDhhmmss}_{DescriptiveName}.cs
//   {YYYYMMDDhhmmss}_{DescriptiveName}.Designer.cs    ← companion file (required)
//
// MigrationId rules (per ADR-008):
//   - Use 14-digit timestamp: YYYYMMDDhhmmss
//   - Phase migrations share a date window: 1 phase = 1 minute (60 slots)
//     Phase 1 launch:  20260628000000 → 20260628000059 (no schema changes anyway)
//     Phase 2 launch:  20260705000000 → 20260705000059
//     Phase 3 launch:  20260712000000 → 20260712000059
//     Phase 4 launch:  20260726000000 → 20260726000059
//     Phase 5 launch:  20260809000000 → 20260809000059
//   - Number within phase = dependency order (parent FK targets first)
//   - Leave ≥ 1 day gap between phases
//   - Cross-module: All DbContexts share public.__EFMigrationsHistory — collisions cause
//     SILENT SKIP. CI smoke test catches duplicates.
//
// Authoring (per memory feedback_migration_manual):
//   - DO NOT use `dotnet ef migrations add` (broken on .NET 10 preview)
//   - Write THIS file + the .Designer.cs companion BY HAND
//   - Update {DbContextName}ModelSnapshot.cs after this migration's changes are
//     reflected in your DbContext OnModelCreating
//   - Use existing migrations as reference:
//     src/Modules/Dispatch/.../Migrations/20260622030000_AddTripVendorPauseSource.cs
//
// Verification (before PR):
//   1. dotnet build --configuration Release
//   2. Reset local DB: docker compose down -v && docker compose up -d postgres
//   3. dotnet run --project src/AMR.DeliveryPlanning.Api
//      → verify auto-applies cleanly, no errors
//   4. Check __EFMigrationsHistory: this MigrationId should appear exactly once
//   5. dotnet test (full suite)
//
// DELETE THIS COMMENT BLOCK before committing the migration.
// =============================================================================

using AMR.DeliveryPlanning.{Module}.Infrastructure.Data;       // ← adjust to your module's DbContext namespace
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.{Module}.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: {1 | 2 | 3 | 4 | 5}
    /// PURPOSE: {One-line description — what schema change and why business needs it}
    /// DEPENDS ON: {Previous migration name(s) — write "none" if first in this DbContext}
    ///             {Cross-module deps if any: e.g. "facility.warehouses must exist (Facility/20260705000000)"}
    /// REVERSIBLE: {Yes | No — if No, explain why (e.g. "data loss on Down — drops column with no source")}
    /// PR: #{PR number after creation}
    /// </summary>
    [DbContext(typeof({YourDbContext}))]
    [Migration("{YYYYMMDDhhmmss}_{DescriptiveName}")]
    public partial class {DescriptiveName} : Migration
    {
        /// <summary>
        /// Forward migration. Should be idempotent where possible
        /// (use CREATE TABLE IF NOT EXISTS, etc. — EF wraps in transaction
        /// so partial failure rolls back, but defensive coding helps debugging).
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- EXAMPLE: Create schema (idempotent) ----
            // migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS {schema_name};");

            // ---- EXAMPLE: Create table using fluent API (preferred) ----
            // migrationBuilder.CreateTable(
            //     name: "your_table",
            //     schema: "your_schema",
            //     columns: table => new
            //     {
            //         Id = table.Column<Guid>(type: "uuid", nullable: false),
            //         Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            //         CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            //     },
            //     constraints: table =>
            //     {
            //         table.PrimaryKey("PK_your_table", x => x.Id);
            //         table.ForeignKey(
            //             name: "FK_your_table_other_table",
            //             column: x => x.OtherId,
            //             principalSchema: "other_schema",
            //             principalTable: "other_table",
            //             principalColumn: "Id",
            //             onDelete: ReferentialAction.Cascade);
            //     });

            // ---- EXAMPLE: Add column ----
            // migrationBuilder.AddColumn<string>(
            //     name: "NewColumn",
            //     schema: "your_schema",
            //     table: "your_table",
            //     type: "character varying(100)",
            //     maxLength: 100,
            //     nullable: true);

            // ---- EXAMPLE: Add index ----
            // migrationBuilder.CreateIndex(
            //     name: "IX_your_table_column",
            //     schema: "your_schema",
            //     table: "your_table",
            //     column: "ColumnName");

            // ---- EXAMPLE: Partial unique index ----
            // migrationBuilder.Sql(@"
            //     CREATE UNIQUE INDEX IF NOT EXISTS ix_your_table_partial
            //     ON your_schema.your_table(column1, column2)
            //     WHERE column2 IS NOT NULL;");

            // ---- EXAMPLE: Add CHECK constraint ----
            // migrationBuilder.Sql(@"
            //     ALTER TABLE your_schema.your_table
            //     ADD CONSTRAINT chk_your_table_invariant
            //     CHECK (column1 IS NULL OR column2 IS NULL);");

            // ---- EXAMPLE: Data backfill (use sparingly — pre-launch is OK) ----
            // migrationBuilder.Sql(@"
            //     UPDATE your_schema.your_table
            //     SET new_column = legacy_column
            //     WHERE new_column IS NULL;");

            // ---- EXAMPLE: Schema rename (preserves data) ----
            // migrationBuilder.Sql("ALTER TABLE old_schema.old_table SET SCHEMA new_schema;");
            // migrationBuilder.Sql("ALTER TABLE new_schema.old_table RENAME TO new_table;");

            throw new System.NotImplementedException("Replace this with actual migration logic");
        }

        /// <summary>
        /// Reverse migration. Per ADR-008, Down() is required but PRE-LAUNCH
        /// we use schema reset instead. Write Down() defensively for future use.
        ///
        /// Mark REVERSIBLE: No in the summary if Down() would lose data.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ---- EXAMPLE: Drop column ----
            // migrationBuilder.DropColumn(
            //     name: "NewColumn",
            //     schema: "your_schema",
            //     table: "your_table");

            // ---- EXAMPLE: Drop table ----
            // migrationBuilder.DropTable(
            //     name: "your_table",
            //     schema: "your_schema");

            // ---- EXAMPLE: Don't drop schema in Down (other migrations may still use it) ----
            // // No DROP SCHEMA here — leave for last-migration-in-schema to handle

            throw new System.NotImplementedException("Replace this with actual rollback logic");
        }
    }
}
