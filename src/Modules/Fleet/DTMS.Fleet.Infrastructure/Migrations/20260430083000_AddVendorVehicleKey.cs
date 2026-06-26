using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorVehicleKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_AdapterKey_VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles",
                columns: new[] { "TenantId", "AdapterKey", "VendorVehicleKey" },
                unique: true,
                filter: "\"VendorVehicleKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_AdapterKey_VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles");
        }
    }
}
