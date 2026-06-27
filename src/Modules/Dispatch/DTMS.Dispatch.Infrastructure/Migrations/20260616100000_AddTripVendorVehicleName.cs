using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Adds VendorVehicleName to dispatch.Trips so the operator UI can
    /// display the vendor's human-readable robot label (e.g. "FAN1_STANDARD_NO5"
    /// from RIOT3 processingVehicle.name) instead of the opaque UUID
    /// stored in VendorVehicleKey. Nullable — pre-existing rows and trips
    /// dispatched before the lifted column carry NULL.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260616100000_AddTripVendorVehicleName")]
    public partial class AddTripVendorVehicleName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorVehicleName",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorVehicleName",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
