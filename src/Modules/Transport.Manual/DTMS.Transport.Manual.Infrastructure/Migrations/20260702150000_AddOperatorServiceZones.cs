using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Manual.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-3 — replace warehouse-scoped operator assignment
    ///   with WMS zone-scoped assignment.
    /// PURPOSE: Add <c>ServiceZones</c> jsonb array of WMS parent-location
    ///   codes (e.g. ["LOC-000149","LOC-000150"]) that this operator can
    ///   handle. ManualDispatchStrategy resolves a pickup location →
    ///   parent code → filters eligible operators by ServiceZones @>
    ///   [parent].
    /// DEFAULT: empty array. Admin UI (PR-4) or an ops one-off UPDATE
    ///   populates zones per operator before Manual dispatch can pick them.
    /// DEPENDS ON: prior Transport.Manual migrations.
    /// REVERSIBLE: Yes — Down() drops the column cleanly. PrimaryWarehouseId
    ///   column stays put so legacy admin UI keeps functioning during the
    ///   PR-3 → PR-4 transition.
    /// </summary>
    [DbContext(typeof(TransportManualDbContext))]
    [Migration("20260702150000_AddOperatorServiceZones")]
    public partial class AddOperatorServiceZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceZones",
                schema: "transportmanual",
                table: "Operators",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            // GIN index for jsonb containment queries (@> and ?). Powers the
            // "operators whose ServiceZones contains this zone" lookup that
            // ManualDispatchStrategy runs on every dispatch decision.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Operators_ServiceZones_gin\" " +
                "ON transportmanual.\"Operators\" USING GIN (\"ServiceZones\" jsonb_path_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS transportmanual.\"IX_Operators_ServiceZones_gin\";");

            migrationBuilder.DropColumn(
                name: "ServiceZones",
                schema: "transportmanual",
                table: "Operators");
        }
    }
}
