using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: RC3 — mission retry visibility.
    /// PURPOSE: Adds dispatch."TripMissionEvents"."Occurrence" (1-based repeat
    ///   counter per (TripId, MissionKey, State)) and widens the idempotency
    ///   unique index to include it. RIOT3 retries a failed sub-task under the
    ///   SAME missionKey, re-emitting PROCESSING/FAILED — the old 3-column
    ///   unique index silently dropped every re-emission, so the retry attempt
    ///   was invisible in the mission timeline (observed live: trip 5018
    ///   E230001 10-minute silent gap, trip eed7f43a E112045 47-second gap).
    /// DEPENDS ON: 20260605040000_AddVendorDetailsCapture (TripMissionEvents table).
    /// REVERSIBLE: Structurally yes — Down() restores the 3-column unique
    ///   index, but it will FAIL if any Occurrence > 1 rows exist (they would
    ///   violate the narrower index). By design: delete retry rows first, or
    ///   prefer rolling back the CODE instead — the new schema is 100%
    ///   compatible with old code (inserts without Occurrence get DEFAULT 1
    ///   and collide with the widened index exactly like the old one).
    /// Backfill semantics: none needed — every existing row is attempt 1 by
    ///   definition (re-emissions were never stored), which is exactly what
    ///   DEFAULT 1 encodes.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260723090000_AddMissionEventOccurrence")]
    public partial class AddMissionEventOccurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Occurrence",
                schema: "dispatch",
                table: "TripMissionEvents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.DropIndex(
                name: "IX_TripMissionEvents_TripId_MissionKey_State",
                schema: "dispatch",
                table: "TripMissionEvents");

            migrationBuilder.CreateIndex(
                name: "IX_TripMissionEvents_TripId_MissionKey_State_Occurrence",
                schema: "dispatch",
                table: "TripMissionEvents",
                columns: new[] { "TripId", "MissionKey", "State", "Occurrence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails when Occurrence > 1 rows exist — see REVERSIBLE note in
            // the header. Delete retry rows first if a schema rollback is
            // genuinely required.
            migrationBuilder.DropIndex(
                name: "IX_TripMissionEvents_TripId_MissionKey_State_Occurrence",
                schema: "dispatch",
                table: "TripMissionEvents");

            migrationBuilder.CreateIndex(
                name: "IX_TripMissionEvents_TripId_MissionKey_State",
                schema: "dispatch",
                table: "TripMissionEvents",
                columns: new[] { "TripId", "MissionKey", "State" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "Occurrence",
                schema: "dispatch",
                table: "TripMissionEvents");
        }
    }
}
