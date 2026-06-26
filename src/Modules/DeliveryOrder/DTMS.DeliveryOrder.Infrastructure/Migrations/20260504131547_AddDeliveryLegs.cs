using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryLegs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "DropLocationCode",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "DropStationId",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "PickupLocationCode",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "PickupStationId",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "DeliveryLegId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderLines_DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "IX_OrderLines_DeliveryLegId");

            migrationBuilder.CreateTable(
                name: "DeliveryLegs",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PickupLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DropLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PickupStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DropStationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryLegs_DeliveryOrders_DeliveryOrderId",
                        column: x => x.DeliveryOrderId,
                        principalSchema: "deliveryorder",
                        principalTable: "DeliveryOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryLegs_DeliveryOrderId",
                schema: "deliveryorder",
                table: "DeliveryLegs",
                column: "DeliveryOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_DeliveryLegs_DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderLines",
                column: "DeliveryLegId",
                principalSchema: "deliveryorder",
                principalTable: "DeliveryLegs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_DeliveryLegs_DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderLines");

            migrationBuilder.DropTable(
                name: "DeliveryLegs",
                schema: "deliveryorder");

            migrationBuilder.RenameColumn(
                name: "DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "DeliveryOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderLines_DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderLines",
                newName: "IX_OrderLines_DeliveryOrderId");

            migrationBuilder.AddColumn<string>(
                name: "DropLocationCode",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DropStationId",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupLocationCode",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PickupStationId",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderLines",
                column: "DeliveryOrderId",
                principalSchema: "deliveryorder",
                principalTable: "DeliveryOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
