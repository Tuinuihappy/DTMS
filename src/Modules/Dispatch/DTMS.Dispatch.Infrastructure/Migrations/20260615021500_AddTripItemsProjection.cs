using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripItemsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripItems",
                schema: "dispatch",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemPk = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrderStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LotNo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemSeq = table.Column<int>(type: "integer", nullable: false),
                    ItemStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PickupCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DropCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WeightKg = table.Column<double>(type: "double precision", nullable: true),
                    BoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastEventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripItems", x => new { x.TripId, x.ItemPk });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripItems_DeliveryOrderId",
                schema: "dispatch",
                table: "TripItems",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TripItems_LotNo",
                schema: "dispatch",
                table: "TripItems",
                column: "LotNo");

            migrationBuilder.CreateIndex(
                name: "IX_TripItems_OrderRef",
                schema: "dispatch",
                table: "TripItems",
                column: "OrderRef");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripItems",
                schema: "dispatch");
        }
    }
}
