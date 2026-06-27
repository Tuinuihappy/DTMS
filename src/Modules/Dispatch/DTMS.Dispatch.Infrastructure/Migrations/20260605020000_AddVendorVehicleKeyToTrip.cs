using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Capture the vendor's raw deviceKey string ("Delta6FAN1", "SEER-001"
    /// etc) on Trip so operator dashboards can show who picked up the
    /// trip without needing a Fleet.Vehicles lookup. Trip.VehicleId
    /// (DTMS Guid) stays untouched — populating it via the vendor
    /// resolver is intentionally deferred (see the webhook
    /// HandleEnvelopeTaskEvent comment).
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260605020000_AddVendorVehicleKeyToTrip")]
    public partial class AddVendorVehicleKeyToTrip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorVehicleKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorVehicleKey",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
