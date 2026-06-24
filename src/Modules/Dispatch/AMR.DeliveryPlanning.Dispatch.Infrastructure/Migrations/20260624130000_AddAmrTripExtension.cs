using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: 3b — Trip extension split.
    /// PURPOSE: Move AMR-specific vendor fields off dispatch.Trips into a
    ///   dedicated dispatch.AmrTripExtensions table (1:0..1). Manual /
    ///   Fleet trips simply don't carry an extension row. Pattern repeats
    ///   for ManualTripExtension (Phase 4) and FleetTripExtension (Phase 5).
    /// DEPENDS ON: 20260623130001_AddTripWarehouseIds (last Dispatch migration).
    /// REVERSIBLE: Yes — Down() restores the four columns on Trips and
    ///   backfills them from AmrTripExtensions before dropping the
    ///   extension table.
    /// Backfill semantics:
    ///   - Only rows where at least one of the 4 vendor fields is non-null
    ///     get an extension row. A trip with all-null vendor fields stays
    ///     extension-less, matching the Manual/Fleet path.
    ///   - Down() reverse: only rows with an extension copy back; missing
    ///     extensions land as all-nulls on Trips, which matches their state
    ///     before this migration applied.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260624130000_AddAmrTripExtension")]
    public partial class AddAmrTripExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AmrTripExtensions",
                schema: "dispatch",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    VendorOrderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VendorVehicleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VendorVehicleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VendorPauseSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmrTripExtensions", x => x.TripId);
                    table.ForeignKey(
                        name: "FK_AmrTripExtensions_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "dispatch",
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill: copy existing vendor data over. Skip trips that
            // had no vendor data — they'd be empty extension rows, which
            // breaks the "AMR-only have an extension" invariant the
            // application code relies on.
            migrationBuilder.Sql(@"
                INSERT INTO dispatch.""AmrTripExtensions""
                    (""TripId"", ""VendorOrderKey"", ""VendorVehicleKey"", ""VendorVehicleName"", ""VendorPauseSource"")
                SELECT
                    ""Id"",
                    ""VendorOrderKey"",
                    ""VendorVehicleKey"",
                    ""VendorVehicleName"",
                    ""VendorPauseSource""
                FROM dispatch.""Trips""
                WHERE ""VendorOrderKey"" IS NOT NULL
                   OR ""VendorVehicleKey"" IS NOT NULL
                   OR ""VendorVehicleName"" IS NOT NULL
                   OR ""VendorPauseSource"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "VendorOrderKey",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VendorVehicleKey",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VendorVehicleName",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VendorPauseSource",
                schema: "dispatch",
                table: "Trips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorOrderKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorVehicleKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorVehicleName",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorPauseSource",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Restore vendor data from the extension table.
            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips"" t
                SET
                    ""VendorOrderKey"" = e.""VendorOrderKey"",
                    ""VendorVehicleKey"" = e.""VendorVehicleKey"",
                    ""VendorVehicleName"" = e.""VendorVehicleName"",
                    ""VendorPauseSource"" = e.""VendorPauseSource""
                FROM dispatch.""AmrTripExtensions"" e
                WHERE t.""Id"" = e.""TripId"";
            ");

            migrationBuilder.DropTable(
                name: "AmrTripExtensions",
                schema: "dispatch");
        }
    }
}
