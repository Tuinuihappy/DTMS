using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FleetUtilizationHourly",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketHour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Active = table.Column<int>(type: "integer", nullable: false),
                    Busy = table.Column<int>(type: "integer", nullable: false),
                    Idle = table.Column<int>(type: "integer", nullable: false),
                    Charging = table.Column<int>(type: "integer", nullable: false),
                    Maintenance = table.Column<int>(type: "integer", nullable: false),
                    LowBattery = table.Column<int>(type: "integer", nullable: false),
                    Offline = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FleetUtilizationHourly", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionInbox",
                schema: "fleet",
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
                name: "VehicleStateHistory",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BatteryLevel = table.Column<double>(type: "double precision", nullable: false),
                    CurrentNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleStateHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FleetUtilizationHourly_BucketHour",
                schema: "fleet",
                table: "FleetUtilizationHourly",
                column: "BucketHour",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionInbox_ProjectorName_EventId",
                schema: "fleet",
                table: "ProjectionInbox",
                columns: new[] { "ProjectorName", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleStateHistory_ToState_OccurredAt",
                schema: "fleet",
                table: "VehicleStateHistory",
                columns: new[] { "ToState", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleStateHistory_VehicleId_OccurredAt",
                schema: "fleet",
                table: "VehicleStateHistory",
                columns: new[] { "VehicleId", "OccurredAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FleetUtilizationHourly",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "ProjectionInbox",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "VehicleStateHistory",
                schema: "fleet");
        }
    }
}
