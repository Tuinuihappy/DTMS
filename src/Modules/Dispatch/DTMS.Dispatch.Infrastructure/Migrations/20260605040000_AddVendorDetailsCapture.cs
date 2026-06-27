using System;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Persist vendor (RIOT3) trip details so DTMS doesn't have to query
    /// the vendor for every detail view, compliance audit, or post-hoc
    /// forensics. Hybrid storage:
    ///   - 3 lifted columns on Trip for hot queries (template name,
    ///     priority, expected completion).
    ///   - 2 jsonb columns for the raw request and final-state response
    ///     (the authoritative compliance record).
    ///   - A new TripMissionEvents table captures per-mission state
    ///     transitions across the trip lifetime. (TripId, MissionKey,
    ///     State) is the idempotency key so webhook + reconciler can
    ///     both write without coordination.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260605040000_AddVendorDetailsCapture")]
    public partial class AddVendorDetailsCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Trip: snapshot columns ──────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "TemplateNameAtDispatch",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriorityAtDispatch",
                schema: "dispatch",
                table: "Trips",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VendorExpectedCompletionAt",
                schema: "dispatch",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorRequestSnapshot",
                schema: "dispatch",
                table: "Trips",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorFinalSnapshot",
                schema: "dispatch",
                table: "Trips",
                type: "jsonb",
                nullable: true);

            // ── TripMissionEvents: per-mission audit ────────────────────
            migrationBuilder.CreateTable(
                name: "TripMissionEvents",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    MissionIndex = table.Column<int>(type: "integer", nullable: false),
                    MissionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MissionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StationName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ChangeStateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripMissionEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripMissionEvents_TripId_MissionKey_State",
                schema: "dispatch",
                table: "TripMissionEvents",
                columns: new[] { "TripId", "MissionKey", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripMissionEvents_TripId_MissionIndex",
                schema: "dispatch",
                table: "TripMissionEvents",
                columns: new[] { "TripId", "MissionIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripMissionEvents",
                schema: "dispatch");

            migrationBuilder.DropColumn(name: "VendorFinalSnapshot",        schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "VendorRequestSnapshot",      schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "VendorExpectedCompletionAt", schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "PriorityAtDispatch",         schema: "dispatch", table: "Trips");
            migrationBuilder.DropColumn(name: "TemplateNameAtDispatch",     schema: "dispatch", table: "Trips");
        }
    }
}
