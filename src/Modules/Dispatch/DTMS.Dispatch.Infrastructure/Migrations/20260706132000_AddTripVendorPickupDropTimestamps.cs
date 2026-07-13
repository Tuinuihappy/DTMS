using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Fire-once markers for the vendor pickup/drop signals. The vendor emits
    /// many missions that finish at the pickup/drop station (a MOVE arrival
    /// plus one or more ACTs), and both the RIOT3 webhook and the reconciler
    /// can observe the same mission. Without a persisted marker,
    /// MarkVendorPickedUp/MarkVendorDropCompleted re-fired their domain events
    /// (double OMS arrive-notify, double audit) and, on a round-trip template,
    /// fired pickup again when the robot returned to the start station.
    ///
    /// A persisted scalar (not the ExecutionEvent history) is required because
    /// the reconciler's loader (GetInFlightEnvelopeTripsAsync) does not
    /// Include(Events) — a scalar is always materialised with the aggregate.
    ///
    /// Adds:
    ///   • <c>VendorPickedUpAt</c> — when the pickup signal first fired.
    ///   • <c>VendorDroppedAt</c>  — when the drop signal first fired.
    ///
    /// Backfill: sets both from the earliest matching ExecutionEvent so trips
    /// already picked/dropped at deploy time don't re-fire once. Both stay NULL
    /// for trips that never reached pickup/drop.
    ///
    /// REVERSIBLE: Yes — Down() drops both columns.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260706132000_AddTripVendorPickupDropTimestamps")]
    public partial class AddTripVendorPickupDropTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VendorPickedUpAt",
                schema: "dispatch",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VendorDroppedAt",
                schema: "dispatch",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill from the earliest matching ExecutionEvent so in-flight
            // trips that already fired pickup/drop before this deploy don't
            // fire a duplicate once the fire-once guard goes live.
            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips"" t
                SET ""VendorPickedUpAt"" = e.min_at
                FROM (
                    SELECT ""TripId"", MIN(COALESCE(""ActedAt"", ""OccurredAt"")) AS min_at
                    FROM dispatch.""ExecutionEvents""
                    WHERE ""EventType"" = 'VendorPickupCompleted'
                    GROUP BY ""TripId""
                ) e
                WHERE e.""TripId"" = t.""Id"" AND t.""VendorPickedUpAt"" IS NULL;");

            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips"" t
                SET ""VendorDroppedAt"" = e.min_at
                FROM (
                    SELECT ""TripId"", MIN(COALESCE(""ActedAt"", ""OccurredAt"")) AS min_at
                    FROM dispatch.""ExecutionEvents""
                    WHERE ""EventType"" = 'VendorDropCompleted'
                    GROUP BY ""TripId""
                ) e
                WHERE e.""TripId"" = t.""Id"" AND t.""VendorDroppedAt"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorDroppedAt",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VendorPickedUpAt",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
