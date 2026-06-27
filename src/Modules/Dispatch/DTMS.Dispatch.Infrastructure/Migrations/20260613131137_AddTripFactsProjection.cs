using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripFactsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bi");

            migrationBuilder.CreateTable(
                name: "TripFacts",
                schema: "bi",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    VendorUpperKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FinalStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PauseCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstPausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastResumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripFacts", x => x.TripId);
                });

            // GENERATED STORED KPI columns. Trip SLA threshold: 2h end-to-end
            // (7200 sec) — most AMR trips inside a single facility close in
            // under 30 minutes; >2h means something stuck on the floor.
            migrationBuilder.Sql(@"
                ALTER TABLE bi.""TripFacts""
                    ADD COLUMN ""TimeToStartSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""StartedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""StartedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""TripFacts""
                    ADD COLUMN ""TimeToCompleteSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""CompletedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""CompletedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""TripFacts""
                    ADD COLUMN ""SlaCompleteBreached"" boolean
                        GENERATED ALWAYS AS (
                            CASE WHEN ""CompletedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""CompletedAt"" - ""CreatedAt"")) > 7200
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_CreatedAt",
                schema: "bi",
                table: "TripFacts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_DeliveryOrderId",
                schema: "bi",
                table: "TripFacts",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_FinalStatus_CreatedAt",
                schema: "bi",
                table: "TripFacts",
                columns: new[] { "FinalStatus", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_SlaCompleteBreached",
                schema: "bi",
                table: "TripFacts",
                column: "SlaCompleteBreached",
                filter: "\"SlaCompleteBreached\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_TripFacts_VendorUpperKey_CreatedAt",
                schema: "bi",
                table: "TripFacts",
                columns: new[] { "VendorUpperKey", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripFacts",
                schema: "bi");
        }
    }
}
