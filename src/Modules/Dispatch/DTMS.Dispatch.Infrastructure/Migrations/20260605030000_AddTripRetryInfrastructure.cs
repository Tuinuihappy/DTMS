using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Trip-level retry infrastructure: route context on Trip
    /// (PickupStationId/DropStationId) so retries can re-resolve the
    /// OrderTemplate without revisiting DeliveryOrder items; attempt
    /// chain (AttemptNumber, PreviousAttemptId) so each retry produces a
    /// distinct UpperKey + traceable lineage; and a separate
    /// TripRetryEvents audit table for immutable enterprise audit
    /// queries (who/when/why for each retry attempt).
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260605030000_AddTripRetryInfrastructure")]
    public partial class AddTripRetryInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Trip: route + attempt chain ──────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "PickupStationId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropStationId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                schema: "dispatch",
                table: "Trips",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousAttemptId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_PreviousAttemptId",
                schema: "dispatch",
                table: "Trips",
                column: "PreviousAttemptId",
                filter: "\"PreviousAttemptId\" IS NOT NULL");

            // ── TripRetryEvents: append-only audit log ───────────────────
            migrationBuilder.CreateTable(
                name: "TripRetryEvents",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    RetrySource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetriedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RetryReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OriginalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripRetryEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripRetryEvents_OriginalTripId",
                schema: "dispatch",
                table: "TripRetryEvents",
                column: "OriginalTripId");

            migrationBuilder.CreateIndex(
                name: "IX_TripRetryEvents_DeliveryOrderId",
                schema: "dispatch",
                table: "TripRetryEvents",
                column: "DeliveryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TripRetryEvents_OccurredAt",
                schema: "dispatch",
                table: "TripRetryEvents",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripRetryEvents",
                schema: "dispatch");

            migrationBuilder.DropIndex(
                name: "IX_Trips_PreviousAttemptId",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(name: "PreviousAttemptId", schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "AttemptNumber",     schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "DropStationId",     schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "PickupStationId",   schema: "dispatch", table: "Trips");
        }
    }
}
