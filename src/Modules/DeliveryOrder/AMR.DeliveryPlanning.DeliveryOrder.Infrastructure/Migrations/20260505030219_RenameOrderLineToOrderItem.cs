using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameOrderLineToOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "OrderLines",
                schema: "deliveryorder",
                newName: "OrderItems",
                newSchema: "deliveryorder");

            migrationBuilder.RenameIndex(
                name: "IX_OrderLines_DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderItems",
                newName: "IX_OrderItems_DeliveryLegId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "OrderItems",
                schema: "deliveryorder",
                newName: "OrderLines",
                newSchema: "deliveryorder");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "IX_OrderLines_DeliveryLegId");
        }
    }
}
