using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStatusHistoryProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectionInbox",
                schema: "dispatch",
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

            migrationBuilder.CreateTable(
                name: "TripStatusHistory",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStatusHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionInbox_ProjectorName_EventId",
                schema: "dispatch",
                table: "ProjectionInbox",
                columns: new[] { "ProjectorName", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripStatusHistory_DeliveryOrderId_OccurredAt",
                schema: "dispatch",
                table: "TripStatusHistory",
                columns: new[] { "DeliveryOrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TripStatusHistory_ToStatus_OccurredAt",
                schema: "dispatch",
                table: "TripStatusHistory",
                columns: new[] { "ToStatus", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TripStatusHistory_TripId_OccurredAt",
                schema: "dispatch",
                table: "TripStatusHistory",
                columns: new[] { "TripId", "OccurredAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectionInbox",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "TripStatusHistory",
                schema: "dispatch");
        }
    }
}
