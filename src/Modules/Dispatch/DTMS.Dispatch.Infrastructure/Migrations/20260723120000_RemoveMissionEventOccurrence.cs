using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: RC3 withdrawal — product decision 2026-07-23.
    /// PURPOSE: Removes dispatch."TripMissionEvents"."Occurrence" and restores
    ///   the original 3-column idempotency unique index. RC3 (migration
    ///   20260723090000, since reverted in code) recorded RIOT3's own retries
    ///   of a failed sub-task as extra rows with Occurrence 2..n; the user
    ///   chose to withdraw the feature entirely and return to first-write-wins
    ///   dedup, where a re-emitted (TripId, MissionKey, State) frame is
    ///   silently dropped.
    /// DEPENDS ON: 20260723090000_AddMissionEventOccurrence being applied.
    ///   That migration's file no longer exists in the assembly (git revert) —
    ///   EF ignores applied history rows without a matching class, so only
    ///   this migration runs.
    /// REVERSIBLE: Yes — Down() re-adds the column (DEFAULT 1) and the
    ///   4-column index.
    /// Data note: verified 0 rows with Occurrence > 1 at authoring time, so
    ///   CreateIndex cannot collide and DropColumn loses nothing. If retry
    ///   rows appear between authoring and deploy, CreateIndex fails — delete
    ///   the Occurrence > 1 rows first (data of the withdrawn feature).
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260723120000_RemoveMissionEventOccurrence")]
    public partial class RemoveMissionEventOccurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
