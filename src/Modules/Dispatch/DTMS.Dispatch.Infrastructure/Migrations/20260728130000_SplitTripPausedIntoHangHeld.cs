using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// TripStatus.Paused is split into Hang (system pause, RIOT3 TASK_HANG)
    /// and Held (operator pause, TASK_HELD / Pause button). The flavour used
    /// to live only in AmrTripExtensions.VendorPauseSource; the status now
    /// carries it so the resume command (CONTINUE_FROM_HANG vs _FROM_HELD,
    /// crossing = vendor E639999) derives from a single source of truth.
    ///
    /// Remaps live rows: Trips.Status='Paused' via a LEFT JOIN on the
    /// extension's VendorPauseSource ('Hang'→'Hang', 'Held'/NULL→'Held' —
    /// Held is the legacy null-source default the old resume handler used).
    /// Same CASE on bi.TripFacts.FinalStatus (current-status column; only
    /// live-paused rows can carry 'Paused'). TripStatusHistory rows are
    /// deliberately NOT rewritten (historical record).
    ///
    /// Recreates the raw-SQL filtered index IX_Trips_ClaimedByOperatorId_Active
    /// whose predicate hard-codes the status list.
    ///
    /// REVERSIBLE: Yes — Down() remaps Hang/Held → 'Paused' (the flavour
    /// survives in the dual-written VendorPauseSource column this release)
    /// and restores the old index predicate.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260728130000_SplitTripPausedIntoHangHeld")]
    public partial class SplitTripPausedIntoHangHeld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS dispatch.""IX_Trips_ClaimedByOperatorId_Active"";");

            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips"" t
                SET ""Status"" = CASE WHEN x.""VendorPauseSource"" = 'Hang' THEN 'Hang' ELSE 'Held' END
                FROM dispatch.""Trips"" t2
                LEFT JOIN dispatch.""AmrTripExtensions"" x ON x.""TripId"" = t2.""Id""
                WHERE t.""Id"" = t2.""Id"" AND t.""Status"" = 'Paused';");

            migrationBuilder.Sql(@"
                UPDATE bi.""TripFacts"" f
                SET ""FinalStatus"" = CASE WHEN x.""VendorPauseSource"" = 'Hang' THEN 'Hang' ELSE 'Held' END
                FROM bi.""TripFacts"" f2
                LEFT JOIN dispatch.""AmrTripExtensions"" x ON x.""TripId"" = f2.""TripId""
                WHERE f.""TripId"" = f2.""TripId"" AND f.""FinalStatus"" = 'Paused';");

            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Trips_ClaimedByOperatorId_Active""
                ON dispatch.""Trips"" (""ClaimedByOperatorId"", ""Status"")
                WHERE ""ClaimedByOperatorId"" IS NOT NULL
                  AND ""Status"" IN ('InProgress', 'Hang', 'Held');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS dispatch.""IX_Trips_ClaimedByOperatorId_Active"";");

            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips""
                SET ""Status"" = 'Paused'
                WHERE ""Status"" IN ('Hang', 'Held');");

            migrationBuilder.Sql(@"
                UPDATE bi.""TripFacts""
                SET ""FinalStatus"" = 'Paused'
                WHERE ""FinalStatus"" IN ('Hang', 'Held');");

            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Trips_ClaimedByOperatorId_Active""
                ON dispatch.""Trips"" (""ClaimedByOperatorId"", ""Status"")
                WHERE ""ClaimedByOperatorId"" IS NOT NULL
                  AND ""Status"" IN ('InProgress', 'Paused');");
        }
    }
}
