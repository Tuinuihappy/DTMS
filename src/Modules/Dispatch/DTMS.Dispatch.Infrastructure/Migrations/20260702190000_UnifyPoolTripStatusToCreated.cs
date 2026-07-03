using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-4b follow-up — Unify Manual/Fleet pool trips onto the
    /// same lifecycle as AMR by dropping the transient <c>Dispatched</c>
    /// status. Pool membership is now derived from
    /// (Status = 'Created' ∧ DispatchedAt IS NOT NULL ∧ ClaimedByOperatorId IS NULL);
    /// nothing on the read side needs to know the intermediate state.
    ///
    /// Fixes:
    ///   1. Backfills any existing Trips left at Status='Dispatched' to
    ///      Status='Created' (pre-launch — no in-flight orders to migrate).
    ///   2. Recreates the partial index IX_Trips_Pool with the new predicate
    ///      so the pool query still hits it O(log n).
    ///
    /// REVERSIBLE: Yes — Down() reverts the WHERE clause. The status
    /// backfill is not reversed (no data to reverse to; the whole point is
    /// that the transient state no longer exists in the codebase).
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260702190000_UnifyPoolTripStatusToCreated")]
    public partial class UnifyPoolTripStatusToCreated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill any lingering pool trips created before the unification.
            migrationBuilder.Sql(
                "UPDATE dispatch.\"Trips\" " +
                "   SET \"Status\" = 'Created' " +
                " WHERE \"Status\" = 'Dispatched';");

            migrationBuilder.Sql("DROP INDEX IF EXISTS dispatch.\"IX_Trips_Pool\";");
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Trips_Pool\" " +
                "ON dispatch.\"Trips\" (\"DispatchedAt\") " +
                "WHERE \"Status\" = 'Created' " +
                  "AND \"DispatchedAt\" IS NOT NULL " +
                  "AND \"ClaimedByOperatorId\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS dispatch.\"IX_Trips_Pool\";");
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Trips_Pool\" " +
                "ON dispatch.\"Trips\" (\"DispatchedAt\") " +
                "WHERE \"Status\" = 'Dispatched' " +
                  "AND \"ClaimedByOperatorId\" IS NULL;");
        }
    }
}
