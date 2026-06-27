using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemPhysicalAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DimsHeightMm",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DimsLengthMm",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DimsWidthMm",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "HandlingInstructions",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<int>(
                name: "HazmatClass",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "TempRangeMaxCelsius",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TempRangeMinCelsius",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DimsHeightMm",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DimsLengthMm",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DimsWidthMm",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "HandlingInstructions",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "HazmatClass",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TempRangeMaxCelsius",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TempRangeMinCelsius",
                schema: "deliveryorder",
                table: "OrderItems");
        }
    }
}
