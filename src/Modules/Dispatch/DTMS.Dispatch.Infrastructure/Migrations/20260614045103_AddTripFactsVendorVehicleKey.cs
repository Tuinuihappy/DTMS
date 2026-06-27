using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripFactsVendorVehicleKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorVehicleKey",
                schema: "bi",
                table: "TripFacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_VendorVehicleKey_CreatedAt",
                schema: "bi",
                table: "TripFacts",
                columns: new[] { "VendorVehicleKey", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripFacts_VendorVehicleKey_CreatedAt",
                schema: "bi",
                table: "TripFacts");

            migrationBuilder.DropColumn(
                name: "VendorVehicleKey",
                schema: "bi",
                table: "TripFacts");
        }
    }
}
