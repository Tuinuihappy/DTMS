using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: 2.2 — first multi-mode transport schema addition (per ADR-002).
    /// PURPOSE: Adds facility.Warehouses table to back the new Warehouse
    ///   aggregate. AMR Map / Station stay untouched here; Phase 2.3 moves
    ///   them under transport_amr schema and adds the WarehouseId FK.
    /// DEPENDS ON: 20260527020000_RefactorStationActionsToMap (last Facility migration).
    /// REVERSIBLE: Yes — Down() drops the table cleanly. Pre-launch so no
    ///   data preservation needed; if the rollback runs against seeded data
    ///   the warehouses are lost (acceptable per ADR-008).
    /// </summary>
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260623120000_AddWarehouseAggregate")]
    public partial class AddWarehouseAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Warehouses",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),

                    // Owned Location (LatLng)
                    LocationLat = table.Column<double>(type: "double precision", nullable: false),
                    LocationLng = table.Column<double>(type: "double precision", nullable: false),

                    // Owned Address — Street required, rest nullable per VO contract
                    AddressStreet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AddressCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressState = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressPostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AddressCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),

                    // Owned OperatingHours — 14 nullable TimeSpan columns (per day open/close)
                    HoursMondayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursMondayClose = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursTuesdayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursTuesdayClose = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursWednesdayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursWednesdayClose = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursThursdayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursThursdayClose = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursFridayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursFridayClose = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursSaturdayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursSaturdayClose = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursSundayOpen = table.Column<TimeSpan>(type: "interval", nullable: true),
                    HoursSundayClose = table.Column<TimeSpan>(type: "interval", nullable: true),

                    // Owned ContactInfo — entirely optional, all columns nullable
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),

                    // Geofence — radius XOR polygon (domain enforces mutual exclusion)
                    GeofenceRadiusM = table.Column<int>(type: "integer", nullable: true),
                    GeofenceAreaWkt = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),

                    // ServiceModes — jsonb array of mode names ("Amr"/"Manual"/"Fleet")
                    ServiceModes = table.Column<string>(type: "jsonb", nullable: false),

                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                schema: "facility",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_IsActive",
                schema: "facility",
                table: "Warehouses",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Warehouses",
                schema: "facility");
        }
    }
}
