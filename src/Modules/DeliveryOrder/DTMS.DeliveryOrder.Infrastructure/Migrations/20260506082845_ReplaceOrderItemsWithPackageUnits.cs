using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceOrderItemsWithPackageUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add CarrierTypeCode with temporary default before dropping LoadUnitType
            migrationBuilder.AddColumn<string>(
                name: "CarrierTypeCode",
                schema: "deliveryorder",
                table: "DeliveryLegs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "DIRECT");

            // Step 2: Migrate LoadUnitType → CarrierTypeCode
            migrationBuilder.Sql("""
                UPDATE deliveryorder."DeliveryLegs"
                SET "CarrierTypeCode" = CASE "LoadUnitType"
                    WHEN 'Shelf'  THEN 'SHELF'
                    WHEN 'Rack'   THEN 'SHELF'
                    WHEN 'Tray'   THEN 'FEEDER'
                    WHEN 'Tugger' THEN 'TUGGER'
                    ELSE 'DIRECT'
                END;
            """);

            // Step 3: Drop old columns and tables
            migrationBuilder.DropTable(
                name: "OrderItems",
                schema: "deliveryorder");

            migrationBuilder.DropColumn(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "DeliveryLegs");

            migrationBuilder.CreateTable(
                name: "PackageUnits",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryLegId = table.Column<Guid>(type: "uuid", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LoadUnitProfileCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GrossWeightKg = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageUnits_DeliveryLegs_DeliveryLegId",
                        column: x => x.DeliveryLegId,
                        principalSchema: "deliveryorder",
                        principalTable: "DeliveryLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageContents",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_PackageContents_PackageUnitId",
                schema: "deliveryorder",
                table: "PackageContents",
                column: "PackageUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageUnits_Barcode",
                schema: "deliveryorder",
                table: "PackageUnits",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageUnits_DeliveryLegId",
                schema: "deliveryorder",
                table: "PackageUnits",
                column: "DeliveryLegId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageContents",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "PackageUnits",
                schema: "deliveryorder");

            migrationBuilder.DropColumn(
                name: "CarrierTypeCode",
                schema: "deliveryorder",
                table: "DeliveryLegs");

            migrationBuilder.AddColumn<string>(
                name: "LoadUnitType",
                schema: "deliveryorder",
                table: "DeliveryLegs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OrderItems",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryLegId = table.Column<Guid>(type: "uuid", nullable: false),
                    HandlingInstructions = table.Column<string[]>(type: "text[]", nullable: false),
                    HazmatClass = table.Column<int>(type: "integer", nullable: true),
                    ItemBarcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ItemDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Line = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LoadUnitType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Weight = table.Column<double>(type: "double precision", nullable: true),
                    WorkOrder = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DimsHeightMm = table.Column<double>(type: "double precision", nullable: true),
                    DimsLengthMm = table.Column<double>(type: "double precision", nullable: true),
                    DimsWidthMm = table.Column<double>(type: "double precision", nullable: true),
                    TempRangeMaxCelsius = table.Column<double>(type: "double precision", nullable: true),
                    TempRangeMinCelsius = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_DeliveryLegs_DeliveryLegId",
                        column: x => x.DeliveryLegId,
                        principalSchema: "deliveryorder",
                        principalTable: "DeliveryLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_DeliveryLegId",
                schema: "deliveryorder",
                table: "OrderItems",
                column: "DeliveryLegId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ItemBarcode",
                schema: "deliveryorder",
                table: "OrderItems",
                column: "ItemBarcode",
                filter: "\"ItemBarcode\" IS NOT NULL");
        }
    }
}
