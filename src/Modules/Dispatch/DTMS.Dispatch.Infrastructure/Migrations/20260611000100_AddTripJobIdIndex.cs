using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260611000100_AddTripJobIdIndex")]
    public partial class AddTripJobIdIndex : Migration
    {
        // Phase b8 — Planning creates a Job anchor per station-pair group and
        // passes its JobId through to CreateEnvelopeTripCommand so the Trip
        // row links back. The column already exists (legacy default
        // Guid.Empty for envelope trips); only the lookup index is new.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Trips_JobId",
                schema: "dispatch",
                table: "Trips",
                column: "JobId",
                filter: "\"JobId\" != '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_JobId",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
