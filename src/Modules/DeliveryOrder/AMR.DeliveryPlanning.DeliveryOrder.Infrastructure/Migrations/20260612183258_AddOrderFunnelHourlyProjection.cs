using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFunnelHourlyProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderFunnelHourly",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketHour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Confirmed = table.Column<int>(type: "integer", nullable: false),
                    Dispatched = table.Column<int>(type: "integer", nullable: false),
                    InProgress = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<int>(type: "integer", nullable: false),
                    PartiallyCompleted = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Cancelled = table.Column<int>(type: "integer", nullable: false),
                    Rejected = table.Column<int>(type: "integer", nullable: false),
                    Held = table.Column<int>(type: "integer", nullable: false),
                    Released = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFunnelHourly", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFunnelHourly_BucketHour",
                schema: "deliveryorder",
                table: "OrderFunnelHourly",
                column: "BucketHour",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderFunnelHourly",
                schema: "deliveryorder");
        }
    }
}
