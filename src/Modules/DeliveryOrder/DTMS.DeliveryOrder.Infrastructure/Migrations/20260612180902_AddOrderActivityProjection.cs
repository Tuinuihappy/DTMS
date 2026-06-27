using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderActivityProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderActivity",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RelatedTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderActivity", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderActivity_OrderId_Category_OccurredAt",
                schema: "deliveryorder",
                table: "OrderActivity",
                columns: new[] { "OrderId", "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderActivity_OrderId_OccurredAt",
                schema: "deliveryorder",
                table: "OrderActivity",
                columns: new[] { "OrderId", "OccurredAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderActivity",
                schema: "deliveryorder");
        }
    }
}
