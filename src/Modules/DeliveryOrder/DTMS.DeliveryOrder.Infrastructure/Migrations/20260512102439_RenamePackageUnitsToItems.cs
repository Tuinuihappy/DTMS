using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenamePackageUnitsToItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackageUnits_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PackageUnits",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.RenameTable(
                name: "PackageUnits",
                schema: "deliveryorder",
                newName: "Items",
                newSchema: "deliveryorder");

            migrationBuilder.RenameIndex(
                name: "IX_PackageUnits_Sku",
                schema: "deliveryorder",
                table: "Items",
                newName: "IX_Items_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_PackageUnits_DeliveryOrderId",
                schema: "deliveryorder",
                table: "Items",
                newName: "IX_Items_DeliveryOrderId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Items",
                schema: "deliveryorder",
                table: "Items",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "Items",
                column: "DeliveryOrderId",
                principalSchema: "deliveryorder",
                principalTable: "DeliveryOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Items",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.RenameTable(
                name: "Items",
                schema: "deliveryorder",
                newName: "PackageUnits",
                newSchema: "deliveryorder");

            migrationBuilder.RenameIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "IX_PackageUnits_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_Items_DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "IX_PackageUnits_DeliveryOrderId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PackageUnits",
                schema: "deliveryorder",
                table: "PackageUnits",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PackageUnits_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits",
                column: "DeliveryOrderId",
                principalSchema: "deliveryorder",
                principalTable: "DeliveryOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
