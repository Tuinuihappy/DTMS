using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobFactsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bi");

            migrationBuilder.CreateTable(
                name: "JobFacts",
                schema: "bi",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedVehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorOrderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FinalStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobFacts", x => x.JobId);
                });

            // GENERATED STORED KPI columns. Job SLA: must be dispatched within
            // 30 min of create (1800 sec) — anything slower means the planner
            // sat on it (vehicle assignment / template resolution issue).
            migrationBuilder.Sql(@"
                ALTER TABLE bi.""JobFacts""
                    ADD COLUMN ""TimeToDispatchSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""DispatchedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""DispatchedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""JobFacts""
                    ADD COLUMN ""TimeToCompleteSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""CompletedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""CompletedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""JobFacts""
                    ADD COLUMN ""SlaDispatchBreached"" boolean
                        GENERATED ALWAYS AS (
                            CASE WHEN ""DispatchedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""DispatchedAt"" - ""CreatedAt"")) > 1800
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_JobFacts_AttemptNumber_CreatedAt",
                schema: "bi",
                table: "JobFacts",
                columns: new[] { "AttemptNumber", "CreatedAt" },
                filter: "\"AttemptNumber\" > 1");

            migrationBuilder.CreateIndex(
                name: "IX_JobFacts_CreatedAt",
                schema: "bi",
                table: "JobFacts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_JobFacts_DeliveryOrderId",
                schema: "bi",
                table: "JobFacts",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFacts_FinalStatus_CreatedAt",
                schema: "bi",
                table: "JobFacts",
                columns: new[] { "FinalStatus", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_JobFacts_SlaDispatchBreached",
                schema: "bi",
                table: "JobFacts",
                column: "SlaDispatchBreached",
                filter: "\"SlaDispatchBreached\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobFacts",
                schema: "bi");
        }
    }
}
