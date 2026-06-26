using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIdOrderNoCreateByLineModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "OrderNo");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrders_TenantId_OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "IX_DeliveryOrders_TenantId_OrderNo");

            migrationBuilder.AddColumn<string>(
                name: "Line",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "deliveryorder",
                table: "OrderLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreateBy",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_OrderId",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_OrderId",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "Line",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "CreateBy",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "OrderId",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "OrderNo",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "OrderKey");

            migrationBuilder.RenameIndex(
                name: "IX_DeliveryOrders_TenantId_OrderNo",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "IX_DeliveryOrders_TenantId_OrderKey");
        }
    }
}
