using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: 3d — Bug #2 fix (vehicle reassignment history).
    /// PURPOSE: Adds dispatch."AmrVehicleAssignments" — append-only history
    ///   of every vehicle the trip was assigned to. Cache pointers on
    ///   AmrTripExtensions (VendorVehicleKey / Name) remain so EF queries
    ///   that filter / project on current vehicle don't need a JOIN.
    /// DEPENDS ON: 20260624130000_AddAmrTripExtension (Phase 3b).
    /// REVERSIBLE: Yes — Down() drops only the new table; cache pointers
    ///   on AmrTripExtensions stay intact. Existing trips' VendorVehicleKey
    ///   isn't touched in either direction.
    /// Backfill semantics:
    ///   - For every AmrTripExtensions row with a non-null VendorVehicleKey,
    ///     insert a Sequence=1 assignment with Source='backfill' and
    ///     AssignedAt = the Trip's StartedAt (fallback CreatedAt).
    ///   - Rows with null VendorVehicleKey (trips that never reached
    ///     TASK_PROCESSING) get no history rows — correct, because no
    ///     vendor ever reported a vehicle for them.
    ///   - Reassignment-history bug fix is forward-only: any historical
    ///     reassignments that previously dropped (first-write-wins
    ///     blocking second webhook) are not recoverable from raw DB.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260624150000_AddAmrVehicleAssignments")]
    public partial class AddAmrVehicleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AmrVehicleAssignments",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    VendorVehicleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VendorVehicleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmrVehicleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmrVehicleAssignments_AmrTripExtensions_TripId",
                        column: x => x.TripId,
                        principalSchema: "dispatch",
                        principalTable: "AmrTripExtensions",
                        principalColumn: "TripId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AmrVehicleAssignments_TripId_Sequence",
                schema: "dispatch",
                table: "AmrVehicleAssignments",
                columns: new[] { "TripId", "Sequence" },
                unique: true);

            // Backfill — one Sequence=1 row per existing AmrTripExtension
            // that has a vehicle key. gen_random_uuid() requires pgcrypto
            // (enabled by default on Postgres 14+); COALESCE picks Trip's
            // StartedAt and falls back to CreatedAt if the trip never started
            // (defensive — those rows shouldn't have a VendorVehicleKey
            // either, so the WHERE clause already excludes them).
            migrationBuilder.Sql(@"
                INSERT INTO dispatch.""AmrVehicleAssignments""
                    (""Id"", ""TripId"", ""Sequence"", ""VendorVehicleKey"",
                     ""VendorVehicleName"", ""AssignedAt"", ""Source"")
                SELECT
                    gen_random_uuid(),
                    e.""TripId"",
                    1,
                    e.""VendorVehicleKey"",
                    e.""VendorVehicleName"",
                    COALESCE(t.""StartedAt"", t.""CreatedAt"", NOW()),
                    'backfill'
                FROM dispatch.""AmrTripExtensions"" e
                JOIN dispatch.""Trips"" t ON t.""Id"" = e.""TripId""
                WHERE e.""VendorVehicleKey"" IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cache pointers on AmrTripExtensions are untouched — Up()
            // didn't drop them, so Down() doesn't need to restore them.
            migrationBuilder.DropTable(
                name: "AmrVehicleAssignments",
                schema: "dispatch");
        }
    }
}
