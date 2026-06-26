using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixItemSkuUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_DeliveryOrderId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DeliveryOrderId_Sku",
                schema: "deliveryorder",
                table: "Items",
                columns: new[] { "DeliveryOrderId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_DeliveryOrderId_Sku",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DeliveryOrderId",
                schema: "deliveryorder",
                table: "Items",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Sku",
                schema: "deliveryorder",
                table: "Items",
                column: "Sku",
                unique: true);
        }
    }
}
