using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkOrderIdItemId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ItemId",
                schema: "deliveryorder",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "WorkOrderId",
                schema: "deliveryorder",
                table: "OrderItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkOrderId",
                schema: "deliveryorder",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
