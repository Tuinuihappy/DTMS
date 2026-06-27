using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStatusCreatedDateSkuIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "Items",
                column: "Sku");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_CreatedDate",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_Status",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_CreatedDate",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_Status",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
