using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_TenantId_AdapterKey_VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "fleet",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "fleet",
                table: "VehicleGroups");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_AdapterKey_VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles",
                columns: new[] { "AdapterKey", "VendorVehicleKey" },
                unique: true,
                filter: "\"VendorVehicleKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_AdapterKey_VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "fleet",
                table: "Vehicles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "fleet",
                table: "VehicleGroups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TenantId_AdapterKey_VendorVehicleKey",
                schema: "fleet",
                table: "Vehicles",
                columns: new[] { "TenantId", "AdapterKey", "VendorVehicleKey" },
                unique: true,
                filter: "\"VendorVehicleKey\" IS NOT NULL");
        }
    }
}
