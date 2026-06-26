using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFactsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bi");

            migrationBuilder.CreateTable(
                name: "OrderFacts",
                schema: "bi",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransportMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FinalStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    TotalQuantity = table.Column<double>(type: "double precision", nullable: false),
                    TotalWeightKg = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InProgressAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PartiallyCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HeldAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFacts", x => x.OrderId);
                });

            // Generated KPI columns. The math lives in the schema, not the
            // projector — single source of truth that EF can read but
            // never write (PropertySaveBehavior.Ignore on the model side).
            // EXTRACT(EPOCH FROM ...) returns double; cast to int for
            // friendly column type. Coalesce-via-WHERE keeps the value
            // NULL until both endpoints of the interval exist.
            migrationBuilder.Sql(@"
                ALTER TABLE bi.""OrderFacts""
                    ADD COLUMN ""TimeToConfirmSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""ConfirmedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""ConfirmedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""OrderFacts""
                    ADD COLUMN ""TimeToDispatchSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""DispatchedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""DispatchedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""OrderFacts""
                    ADD COLUMN ""TimeToCompleteSec"" integer
                        GENERATED ALWAYS AS (
                            CASE WHEN ""CompletedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""CompletedAt"" - ""CreatedAt""))::integer
                                ELSE NULL END
                        ) STORED;
            ");

            // SLA thresholds: ConfirmedAt within 4h of CreatedAt (14400 sec),
            // CompletedAt within 24h (86400 sec). When the timestamp is NULL
            // (status not reached yet) the breach flag is NULL — caller can
            // treat NULL as "not yet evaluable".
            migrationBuilder.Sql(@"
                ALTER TABLE bi.""OrderFacts""
                    ADD COLUMN ""SlaConfirmBreached"" boolean
                        GENERATED ALWAYS AS (
                            CASE WHEN ""ConfirmedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""ConfirmedAt"" - ""CreatedAt"")) > 14400
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE bi.""OrderFacts""
                    ADD COLUMN ""SlaCompleteBreached"" boolean
                        GENERATED ALWAYS AS (
                            CASE WHEN ""CompletedAt"" IS NOT NULL AND ""CreatedAt"" IS NOT NULL
                                THEN EXTRACT(EPOCH FROM (""CompletedAt"" - ""CreatedAt"")) > 86400
                                ELSE NULL END
                        ) STORED;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_CreatedAt",
                schema: "bi",
                table: "OrderFacts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_FinalStatus_CreatedAt",
                schema: "bi",
                table: "OrderFacts",
                columns: new[] { "FinalStatus", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_Priority_CreatedAt",
                schema: "bi",
                table: "OrderFacts",
                columns: new[] { "Priority", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_SlaConfirmBreached",
                schema: "bi",
                table: "OrderFacts",
                column: "SlaConfirmBreached",
                filter: "\"SlaConfirmBreached\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFacts_SourceSystem_CreatedAt",
                schema: "bi",
                table: "OrderFacts",
                columns: new[] { "SourceSystem", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderFacts",
                schema: "bi");
        }
    }
}
