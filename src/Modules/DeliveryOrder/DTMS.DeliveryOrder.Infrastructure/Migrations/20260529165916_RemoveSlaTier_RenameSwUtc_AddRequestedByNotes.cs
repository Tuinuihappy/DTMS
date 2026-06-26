using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSlaTier_RenameSwUtc_AddRequestedByNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "ServiceWindow_Latest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "ServiceWindow_LatestUtc");

            migrationBuilder.RenameColumn(
                name: "ServiceWindow_Earliest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "ServiceWindow_EarliestUtc");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBy",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "ServiceWindow_LatestUtc",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "ServiceWindow_Latest");

            migrationBuilder.RenameColumn(
                name: "ServiceWindow_EarliestUtc",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "ServiceWindow_Earliest");

            migrationBuilder.AddColumn<string>(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }
    }
}
