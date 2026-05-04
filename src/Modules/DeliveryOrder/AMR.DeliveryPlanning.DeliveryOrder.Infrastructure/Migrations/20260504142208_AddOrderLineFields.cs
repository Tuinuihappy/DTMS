using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ItemCode",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "WorkOrder");

            migrationBuilder.AddColumn<string>(
                name: "ItemDescription",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemId",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemNumber",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ItemStatus",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkOrderId",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ItemDescription",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ItemId",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ItemNumber",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ItemStatus",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "WorkOrderId",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.RenameColumn(
                name: "WorkOrder",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "ItemCode");
        }
    }
}
