using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fleet");

            migrationBuilder.CreateTable(
                name: "ChargingPolicies",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LowThresholdPct = table.Column<double>(type: "double precision", nullable: false),
                    TargetThresholdPct = table.Column<double>(type: "double precision", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Technician = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleGroups",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VehicleTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BatteryLevel = table.Column<double>(type: "double precision", nullable: false),
                    CurrentNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdapterKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "riot3")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTypes",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaxPayload = table.Column<double>(type: "double precision", nullable: false),
                    Capabilities = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleGroupMembers",
                schema: "fleet",
                columns: table => new
                {
                    VehicleGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleGroupMembers", x => new { x.VehicleGroupId, x.VehicleId });
                    table.ForeignKey(
                        name: "FK_VehicleGroupMembers_VehicleGroups_VehicleGroupId",
                        column: x => x.VehicleGroupId,
                        principalSchema: "fleet",
                        principalTable: "VehicleGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VehicleGroupMembers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalSchema: "fleet",
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleGroupMembers_VehicleId",
                schema: "fleet",
                table: "VehicleGroupMembers",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargingPolicies",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "MaintenanceRecords",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "VehicleGroupMembers",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "VehicleTypes",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "VehicleGroups",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "Vehicles",
                schema: "fleet");
        }
    }
}
