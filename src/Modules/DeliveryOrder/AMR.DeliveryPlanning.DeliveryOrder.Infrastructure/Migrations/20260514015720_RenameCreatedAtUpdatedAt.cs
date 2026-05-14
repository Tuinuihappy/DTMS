using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameCreatedAtUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "UpdatedDate");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "CreatedDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedDate",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedDate",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "CreatedAt");
        }
    }
}
