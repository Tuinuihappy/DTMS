using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deliveryorder");

            migrationBuilder.CreateTable(
                name: "DeliveryOrders",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PickupLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DropLocationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SLA = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickupStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DropStationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderAmendments",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    NewSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    AmendedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AmendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAmendments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderAuditEvents",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLines_DeliveryOrders_DeliveryOrderId",
                        column: x => x.DeliveryOrderId,
                        principalSchema: "deliveryorder",
                        principalTable: "DeliveryOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSchedules",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
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
                name: "IX_DeliveryOrders_OrderKey",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                column: "OrderKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderAmendments_DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderAmendments",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAuditEvents_DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderAuditEvents",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_DeliveryOrderId",
                schema: "deliveryorder",
                table: "OrderLines",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_DeliveryOrderId",
                schema: "deliveryorder",
                table: "RecurringSchedules",
                column: "DeliveryOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderAmendments",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "OrderAuditEvents",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "OrderLines",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "RecurringSchedules",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "DeliveryOrders",
                schema: "deliveryorder");
        }
    }
}
