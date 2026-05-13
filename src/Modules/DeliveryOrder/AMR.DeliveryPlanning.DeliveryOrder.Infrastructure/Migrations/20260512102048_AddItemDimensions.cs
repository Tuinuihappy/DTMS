using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackageUnits_DeliveryLegs_DeliveryLegId",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropTable(
                name: "DeliveryLegs",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "PackageContents",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "RecurringSchedules",
                schema: "deliveryorder");

            migrationBuilder.DropColumn(
                name: "ServiceWindowEarliest",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "SlaTier",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "Tags",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "LoadUnitProfileCode",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "PickupLocationCode");

            migrationBuilder.RenameColumn(
                name: "GrossWeightKg",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "WeightKg");

            migrationBuilder.RenameColumn(
                name: "DeliveryLegId",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "DeliveryOrderId");

            migrationBuilder.RenameColumn(
                name: "Barcode",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "Sku");

            migrationBuilder.RenameIndex(
                name: "IX_PackageUnits_DeliveryLegId",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "IX_PackageUnits_DeliveryOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_PackageUnits_Barcode",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "IX_PackageUnits_Sku");

            migrationBuilder.RenameColumn(
                name: "StructureType",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "Priority");

            migrationBuilder.RenameColumn(
                name: "ServiceWindowLatest",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "RequestedTime");

            migrationBuilder.RenameColumn(
                name: "OrderName",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "OrderRef");

            migrationBuilder.AddColumn<string>(
                name: "DropLocationCode",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DropStationId",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HeightCm",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LengthCm",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PickupStationId",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Quantity",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Uom",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "WidthCm",
                schema: "deliveryorder",
                table: "PackageUnits",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CargoType",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_PackageUnits_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits",
                column: "DeliveryOrderId",
                principalSchema: "deliveryorder",
                principalTable: "DeliveryOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackageUnits_DeliveryOrders_DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "DropLocationCode",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "DropStationId",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "PickupStationId",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "Quantity",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "Uom",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                schema: "deliveryorder",
                table: "PackageUnits");

            migrationBuilder.DropColumn(
                name: "CargoType",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            migrationBuilder.RenameColumn(
                name: "WeightKg",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "GrossWeightKg");

            migrationBuilder.RenameColumn(
                name: "Sku",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "Barcode");

            migrationBuilder.RenameColumn(
                name: "PickupLocationCode",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "LoadUnitProfileCode");

            migrationBuilder.RenameColumn(
                name: "DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "DeliveryLegId");

            migrationBuilder.RenameIndex(
                name: "IX_PackageUnits_Sku",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "IX_PackageUnits_Barcode");

            migrationBuilder.RenameIndex(
                name: "IX_PackageUnits_DeliveryOrderId",
                schema: "deliveryorder",
                table: "PackageUnits",
                newName: "IX_PackageUnits_DeliveryLegId");

            migrationBuilder.RenameColumn(
                name: "RequestedTime",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "ServiceWindowLatest");

            migrationBuilder.RenameColumn(
                name: "Priority",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "StructureType");

            migrationBuilder.RenameColumn(
                name: "OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "OrderName");

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

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "text[]",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "DeliveryLegs",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierTypeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DropLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DropStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PickupLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PickupStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "PackageContents",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PackageUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageContents_PackageUnits_PackageUnitId",
                        column: x => x.PackageUnitId,
                        principalSchema: "deliveryorder",
                        principalTable: "PackageUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSchedules",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSchedules_DeliveryOrders_DeliveryOrderId",
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

            migrationBuilder.CreateIndex(
                name: "IX_PackageContents_PackageUnitId",
                schema: "deliveryorder",
                table: "PackageContents",
                column: "PackageUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_DeliveryOrderId",
                schema: "deliveryorder",
                table: "RecurringSchedules",
                column: "DeliveryOrderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackageUnits_DeliveryLegs_DeliveryLegId",
                schema: "deliveryorder",
                table: "PackageUnits",
                column: "DeliveryLegId",
                principalSchema: "deliveryorder",
                principalTable: "DeliveryLegs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
