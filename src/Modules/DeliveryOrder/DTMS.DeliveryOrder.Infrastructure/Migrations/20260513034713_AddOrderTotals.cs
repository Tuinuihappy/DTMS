using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TotalQuantity",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TotalWeightKg",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalQuantity",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "TotalWeightKg",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
