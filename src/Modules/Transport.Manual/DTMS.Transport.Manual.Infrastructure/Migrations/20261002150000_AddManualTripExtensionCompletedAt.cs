using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Manual.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-10-02 — Add <c>ManualTripExtensions.CompletedAt</c>, set by
    /// CompleteTrip. The operator app's assigned-trips list keeps finished
    /// trips as history, and without this column it could not tell a
    /// completed trip from one still waiting on "Complete trip".
    ///
    /// Backfills from <c>dispatch.Trips.CompletedAt</c> for extensions whose
    /// trip is already Completed.
    ///
    /// REVERSIBLE: Yes — Down() drops the nullable column.
    /// </summary>
    [DbContext(typeof(TransportManualDbContext))]
    [Migration("20261002150000_AddManualTripExtensionCompletedAt")]
    public partial class AddManualTripExtensionCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.DateTime>(
                name: "CompletedAt",
                schema: "transportmanual",
                table: "ManualTripExtensions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE transportmanual."ManualTripExtensions" e
                SET "CompletedAt" = COALESCE(t."CompletedAt", e."DroppedAt")
                FROM dispatch."Trips" t
                WHERE t."Id" = e."TripId"
                  AND t."Status" = 'Completed'
                  AND e."DroppedAt" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "transportmanual",
                table: "ManualTripExtensions");
        }
    }
}
