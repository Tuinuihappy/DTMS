using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemBarcodeToOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ItemBarcode",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ItemBarcode",
                schema: "deliveryorder",
                table: "OrderItems",
                column: "ItemBarcode",
                filter: "\"ItemBarcode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderItems_ItemBarcode",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ItemBarcode",
                schema: "deliveryorder",
                table: "OrderItems");
        }
    }
}
