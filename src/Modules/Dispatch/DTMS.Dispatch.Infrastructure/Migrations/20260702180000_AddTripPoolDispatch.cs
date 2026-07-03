using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-4b (foundation) — schema for the Manual/Fleet pool
    /// dispatch model. Trip stays in <c>Created</c> status (parity with AMR)
    /// and sits in a shared pool once <c>DispatchedAt</c> is stamped, until
    /// an operator wins the acknowledge CAS on their PWA.
    ///
    /// Adds:
    ///   • <c>ClaimedByOperatorId</c> — set when an operator wins the CAS
    ///     race for a pooled trip.
    ///   • <c>ClaimedAt</c> — mirror of the timestamp so admin queries
    ///     don't have to join the Operators table.
    ///   • <c>DispatchedAt</c> — "trip is in the pool" signal + FIFO
    ///     ordering key (oldest first).
    ///   • Partial index <c>IX_Trips_Pool</c> — powers the hot pool query
    ///     without indexing every trip row.
    ///
    /// Pool membership predicate:
    /// (Status = 'Created' ∧ DispatchedAt IS NOT NULL ∧ ClaimedByOperatorId IS NULL).
    ///
    /// REVERSIBLE: Yes — Down() drops all 3 columns + the partial index.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260702180000_AddTripPoolDispatch")]
    public partial class AddTripPoolDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimedByOperatorId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                schema: "dispatch",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedAt",
                schema: "dispatch",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            // Partial composite index — the pool query filters by the
            // (Status='Created' ∧ DispatchedAt IS NOT NULL ∧ ClaimedByOperatorId
            // IS NULL) predicate. A full index on any single column would
            // waste space on the AMR-heavy majority of trips (AMR keeps
            // DispatchedAt NULL — it doesn't go through the pool). The
            // partial WHERE clause is what makes the pool query O(log n)
            // even with millions of terminal trips in the table.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Trips_Pool\" " +
                "ON dispatch.\"Trips\" (\"DispatchedAt\") " +
                "WHERE \"Status\" = 'Created' " +
                  "AND \"DispatchedAt\" IS NOT NULL " +
                  "AND \"ClaimedByOperatorId\" IS NULL;");

            // Reverse lookup for "trips I'm working on right now" — the
            // operator PWA hits this on every /me refresh + pool
            // invalidation. Filter narrows to the (multi-active) working
            // set so the majority of terminal rows don't inflate it.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Trips_ClaimedByOperatorId_Active\" " +
                "ON dispatch.\"Trips\" (\"ClaimedByOperatorId\", \"Status\") " +
                "WHERE \"ClaimedByOperatorId\" IS NOT NULL " +
                  "AND \"Status\" IN ('InProgress', 'Paused');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS dispatch.\"IX_Trips_ClaimedByOperatorId_Active\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS dispatch.\"IX_Trips_Pool\";");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ClaimedByOperatorId",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
