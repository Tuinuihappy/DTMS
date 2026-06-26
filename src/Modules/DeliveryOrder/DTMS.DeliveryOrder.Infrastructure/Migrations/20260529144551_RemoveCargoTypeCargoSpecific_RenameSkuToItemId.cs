using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCargoTypeCargoSpecific_RenameSkuToItemId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "CargoType",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DateCode",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "InventoryNo",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Line",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LotNo",
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

            migrationBuilder.DropColumn(
                name: "Wo",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.RenameColumn(
                name: "Sku",
                schema: "deliveryorder",
                table: "Items",
                newName: "ItemId");

            migrationBuilder.AlterColumn<string>(
                name: "Uom",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Quantity",
                schema: "deliveryorder",
                table: "Items",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_DeliveryOrderId_ItemId",
                schema: "deliveryorder",
                table: "Items",
                columns: new[] { "DeliveryOrderId", "ItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_DeliveryOrderId_ItemId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                schema: "deliveryorder",
                table: "Items",
                newName: "Sku");

            migrationBuilder.AlterColumn<string>(
                name: "Uom",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<double>(
                name: "Quantity",
                schema: "deliveryorder",
                table: "Items",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AddColumn<string>(
                name: "CargoType",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

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
                name: "Line",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotNo",
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

            migrationBuilder.AddColumn<string>(
                name: "Wo",
                schema: "deliveryorder",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "Items",
                column: "Sku");
        }
    }
}
