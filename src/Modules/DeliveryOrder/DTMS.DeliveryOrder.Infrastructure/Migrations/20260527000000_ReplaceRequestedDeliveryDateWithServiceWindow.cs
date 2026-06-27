using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRequestedDeliveryDateWithServiceWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the two ServiceWindow columns as an EF OwnsOne value object. Both bounds
            // are nullable independently — at least one must be set, but that invariant is
            // enforced in the domain factory, not at the DB level.
            migrationBuilder.AddColumn<System.DateTime>(
                name: "ServiceWindow_Earliest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "ServiceWindow_Latest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: an existing RequestedDeliveryDate semantically meant "ship-not-after",
            // so it maps to ServiceWindow.Latest with no lower bound.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                SET ""ServiceWindow_Latest"" = ""RequestedDeliveryDate""
                WHERE ""RequestedDeliveryDate"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "RequestedDeliveryDate",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.DateTime>(
                name: "RequestedDeliveryDate",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                SET ""RequestedDeliveryDate"" = ""ServiceWindow_Latest""
                WHERE ""ServiceWindow_Latest"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "ServiceWindow_Latest",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "ServiceWindow_Earliest",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
