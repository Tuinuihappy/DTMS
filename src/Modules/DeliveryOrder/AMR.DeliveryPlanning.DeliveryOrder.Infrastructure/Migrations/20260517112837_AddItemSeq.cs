using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_DeliveryOrderId_Sku",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.AddColumn<int>(
                name: "ItemSeq",
                schema: "deliveryorder",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Items_DeliveryOrderId_ItemSeq",
                schema: "deliveryorder",
                table: "Items",
                columns: new[] { "DeliveryOrderId", "ItemSeq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_DeliveryOrderId_ItemSeq",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ItemSeq",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.CreateIndex(
                name: "IX_Items_DeliveryOrderId_Sku",
                schema: "deliveryorder",
                table: "Items",
                columns: new[] { "DeliveryOrderId", "Sku" },
                unique: true);
        }
    }
}
