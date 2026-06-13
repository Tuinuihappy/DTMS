using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderListViewProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderListView",
                schema: "deliveryorder",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransportMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    HasFailedTrip = table.Column<bool>(type: "boolean", nullable: false),
                    HasActiveJob = table.Column<bool>(type: "boolean", nullable: false),
                    LatestTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestJobStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    TotalQuantity = table.Column<double>(type: "double precision", nullable: false),
                    TotalWeightKg = table.Column<double>(type: "double precision", nullable: false),
                    RequiresDropPod = table.Column<bool>(type: "boolean", nullable: true),
                    RequiresPickupPod = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ServiceWindowEarliestUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ServiceWindowLatestUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SearchText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderListView", x => x.OrderId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderListView_HasActiveJob",
                schema: "deliveryorder",
                table: "OrderListView",
                column: "HasActiveJob",
                filter: "\"HasActiveJob\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_OrderListView_HasFailedTrip",
                schema: "deliveryorder",
                table: "OrderListView",
                column: "HasFailedTrip",
                filter: "\"HasFailedTrip\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_OrderListView_OrderRef",
                schema: "deliveryorder",
                table: "OrderListView",
                column: "OrderRef");

            migrationBuilder.CreateIndex(
                name: "IX_OrderListView_Priority_CreatedAt",
                schema: "deliveryorder",
                table: "OrderListView",
                columns: new[] { "Priority", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OrderListView_Status_CreatedAt",
                schema: "deliveryorder",
                table: "OrderListView",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });

            // ── Phase P4 — tsvector + GIN index for full-text search ──
            // Postgres-only feature; EF Core doesn't model tsvector
            // first-class so we add it via raw SQL. The column is
            // GENERATED STORED so Postgres recomputes it automatically
            // every time SearchText changes — the projector never
            // touches SearchVector directly.
            migrationBuilder.Sql(@"
                ALTER TABLE deliveryorder.""OrderListView""
                    ADD COLUMN ""SearchVector"" tsvector
                    GENERATED ALWAYS AS (to_tsvector('simple', coalesce(""SearchText"", ''))) STORED;
            ");
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_OrderListView_SearchVector""
                    ON deliveryorder.""OrderListView""
                    USING GIN(""SearchVector"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS deliveryorder.""IX_OrderListView_SearchVector"";");
            // GENERATED columns drop with the table; explicit ALTER not needed
            migrationBuilder.DropTable(
                name: "OrderListView",
                schema: "deliveryorder");
        }
    }
}
