using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStatusHistoryProjection : Migration
    {
        // Phase P1 (b12) — Order status history projection. Adds two
        // tables in the deliveryorder schema:
        //   - OrderStatusHistory: read model, one row per status transition,
        //     written by OrderStatusHistoryProjector.
        //   - ProjectionInbox: idempotency bookkeeping, one row per
        //     (projector, event) pair processed by ANY projector that
        //     lives in the DeliveryOrder module.
        //
        // The Designer file generated alongside this migration also
        // re-asserts a handful of pre-existing columns + the ItemPodEvents
        // table because earlier migrations were hand-authored without
        // Designer snapshots. Those re-assertions are stripped out of the
        // Up/Down methods here — only the P1 additions ship.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderStatusHistory",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionInbox",
                schema: "deliveryorder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionInbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_OrderId_OccurredAt",
                schema: "deliveryorder",
                table: "OrderStatusHistory",
                columns: new[] { "OrderId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_ToStatus_OccurredAt",
                schema: "deliveryorder",
                table: "OrderStatusHistory",
                columns: new[] { "ToStatus", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionInbox_ProjectorName_EventId",
                schema: "deliveryorder",
                table: "ProjectionInbox",
                columns: new[] { "ProjectorName", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderStatusHistory",
                schema: "deliveryorder");

            migrationBuilder.DropTable(
                name: "ProjectionInbox",
                schema: "deliveryorder");
        }
    }
}
