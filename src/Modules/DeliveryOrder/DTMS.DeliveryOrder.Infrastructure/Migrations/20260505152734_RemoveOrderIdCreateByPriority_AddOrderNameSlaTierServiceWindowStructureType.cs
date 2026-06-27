using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOrderIdCreateByPriority_AddOrderNameSlaTierServiceWindowStructureType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_OrderId",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "CreateBy",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "OrderId",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "SLA",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "ServiceWindowLatest");

            migrationBuilder.RenameColumn(
                name: "Priority",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "StructureType");

            migrationBuilder.AddColumn<string>(
                name: "OrderName",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ServiceWindowEarliest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderName",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "ServiceWindowEarliest",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "StructureType",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "Priority");

            migrationBuilder.RenameColumn(
                name: "ServiceWindowLatest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "SLA");

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
    }
}
