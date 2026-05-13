using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCargoSpecific : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryNo",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartNo",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Po",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradingCode",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vendor",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateCode",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "InventoryNo",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PartNo",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Po",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TraceId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TradingCode",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Vendor",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
