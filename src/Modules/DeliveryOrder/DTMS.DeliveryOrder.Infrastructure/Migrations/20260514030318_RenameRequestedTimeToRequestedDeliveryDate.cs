using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameRequestedTimeToRequestedDeliveryDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequestedTime",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "RequestedDeliveryDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequestedDeliveryDate",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "RequestedTime");
        }
    }
}
