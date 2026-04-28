using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planning");

            migrationBuilder.CreateTable(
                name: "CostModelConfigs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleTypeKey = table.Column<string>(type: "text", nullable: true),
                    TravelDistanceWeight = table.Column<double>(type: "double precision", nullable: false),
                    BatteryBurnWeight = table.Column<double>(type: "double precision", nullable: false),
                    SlaPenaltyWeight = table.Column<double>(type: "double precision", nullable: false),
                    LowBatteryThresholdPct = table.Column<double>(type: "double precision", nullable: false),
                    CriticalBatteryThresholdPct = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostModelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobDependencies",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PredecessorJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuccessorJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependencyType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MinimumDwell = table.Column<TimeSpan>(type: "interval", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobDependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssignedVehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstimatedDuration = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedDistance = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequiredCapability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalWeight = table.Column<double>(type: "double precision", nullable: false),
                    PlanningTrace = table.Column<string>(type: "text", nullable: true),
                    SlaDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DerivedFromOrders = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MilkRunTemplates",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CronSchedule = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilkRunTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Legs",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCost = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Legs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Legs_Jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "planning",
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MilkRunStops",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    PlannedArrivalOffset = table.Column<TimeSpan>(type: "interval", nullable: true),
                    DwellTime = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilkRunStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MilkRunStops_MilkRunTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "planning",
                        principalTable: "MilkRunTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Stops",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegId = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    ExpectedArrival = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stops_Legs_LegId",
                        column: x => x.LegId,
                        principalSchema: "planning",
                        principalTable: "Legs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostModelConfigs_VehicleTypeKey",
                schema: "planning",
                table: "CostModelConfigs",
                column: "VehicleTypeKey",
                unique: true,
                filter: "\"VehicleTypeKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobDependencies_PredecessorJobId",
                schema: "planning",
                table: "JobDependencies",
                column: "PredecessorJobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobDependencies_SuccessorJobId",
                schema: "planning",
                table: "JobDependencies",
                column: "SuccessorJobId");

            migrationBuilder.CreateIndex(
                name: "IX_Legs_JobId",
                schema: "planning",
                table: "Legs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_MilkRunStops_TemplateId",
                schema: "planning",
                table: "MilkRunStops",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Stops_LegId",
                schema: "planning",
                table: "Stops",
                column: "LegId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostModelConfigs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "JobDependencies",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "MilkRunStops",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "Stops",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "MilkRunTemplates",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "Legs",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "Jobs",
                schema: "planning");
        }
    }
}
