using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixTenantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_TenantId_OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                columns: new[] { "TenantId", "OrderKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_TenantId_OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                column: "OrderKey",
                unique: true);
        }
    }
}
