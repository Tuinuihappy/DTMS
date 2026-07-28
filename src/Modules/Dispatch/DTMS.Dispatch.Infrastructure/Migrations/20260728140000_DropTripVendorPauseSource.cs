using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Hang/Held cleanup (contract phase). The pause flavour now lives on
    /// Trip.Status; the dual-written VendorPauseSource column existed only
    /// as the rollback path for 20260728130000_SplitTripPausedIntoHangHeld
    /// and is dropped once that split is verified in production. The old
    /// TripPausedIntegrationEventV1 shims were deleted in the same release
    /// (outbox/DLQ verified at 0 rows of the old CLR type name).
    ///
    /// REVERSIBLE: structurally — Down() re-adds the column and backfills
    /// it from the current Status for live Hang/Held trips. Beyond that the
    /// Hang/Held split itself is roll-forward only.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260728140000_DropTripVendorPauseSource")]
    public partial class DropTripVendorPauseSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorPauseSource",
                schema: "dispatch",
                table: "AmrTripExtensions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorPauseSource",
                schema: "dispatch",
                table: "AmrTripExtensions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Best-effort backfill from the status that superseded it.
            migrationBuilder.Sql(@"
                UPDATE dispatch.""AmrTripExtensions"" x
                SET ""VendorPauseSource"" = t.""Status""
                FROM dispatch.""Trips"" t
                WHERE t.""Id"" = x.""TripId"" AND t.""Status"" IN ('Hang', 'Held');");
        }
    }
}
