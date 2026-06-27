using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobStatusHistoryProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobStatusHistory",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStatusHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionInbox",
                schema: "planning",
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
                name: "IX_JobStatusHistory_DeliveryOrderId_OccurredAt",
                schema: "planning",
                table: "JobStatusHistory",
                columns: new[] { "DeliveryOrderId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobStatusHistory_JobId_OccurredAt",
                schema: "planning",
                table: "JobStatusHistory",
                columns: new[] { "JobId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_JobStatusHistory_ToStatus_OccurredAt",
                schema: "planning",
                table: "JobStatusHistory",
                columns: new[] { "ToStatus", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionInbox_ProjectorName_EventId",
                schema: "planning",
                table: "ProjectionInbox",
                columns: new[] { "ProjectorName", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobStatusHistory",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "ProjectionInbox",
                schema: "planning");
        }
    }
}
