using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// 2026-08 — denormalizes the source system's own location code strings
    /// onto the Trip (PickupLocationCode / DropLocationCode), frozen at
    /// create time. The shipment.pickedup/droppedoff callbacks and their
    /// resends read these instead of scanning the order's items, so a resend
    /// keeps working after a cancel unbinds the items (Item.TripId cleared)
    /// and a trip whose items somehow carry mixed codes still reports ITS
    /// location.
    ///
    /// <para>Backfill, two tiers, both idempotent (only touch NULL rows):
    /// tier 1 from deliveryorder.Items still bound to the trip; tier 2 from
    /// the dispatch.TripItems projection (survives unbinding — covers
    /// cancelled trips). Rows reachable by neither (cancelled before start,
    /// never bound) stay NULL — such trips have nothing to resend anyway
    /// (VendorPickedUpAt guard), and the callback code falls back to an item
    /// scan for NULLs.</para>
    ///
    /// REVERSIBLE: Yes — Down() drops both columns.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260812100000_AddTripLocationCodes")]
    public partial class AddTripLocationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PickupLocationCode",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DropLocationCode",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Tier 1 — items still bound to the trip (Items.TripId survives
            // everything except cancel's unbind). One row per trip: codes are
            // homogeneous per trip by the dispatch grouping key, DISTINCT ON
            // just collapses the item rows.
            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips"" t
                   SET ""PickupLocationCode"" = i.""PickupLocationCode"",
                       ""DropLocationCode""   = i.""DropLocationCode""
                  FROM (SELECT DISTINCT ON (""TripId"") ""TripId"", ""PickupLocationCode"", ""DropLocationCode""
                          FROM deliveryorder.""Items""
                         WHERE ""TripId"" IS NOT NULL
                           AND ""PickupLocationCode"" <> ''
                           AND ""DropLocationCode"" <> ''
                         ORDER BY ""TripId"") i
                 WHERE i.""TripId"" = t.""Id""
                   AND t.""PickupLocationCode"" IS NULL;
            ");

            // Tier 2 — the TripItems projection keeps its rows after an
            // unbind, so it reaches trips tier 1 cannot (cancelled after
            // start). PickupCode/DropCode are nullable on legacy/test rows —
            // filter them out so we never overwrite with NULL.
            migrationBuilder.Sql(@"
                UPDATE dispatch.""Trips"" t
                   SET ""PickupLocationCode"" = p.""PickupCode"",
                       ""DropLocationCode""   = p.""DropCode""
                  FROM (SELECT DISTINCT ON (""TripId"") ""TripId"", ""PickupCode"", ""DropCode""
                          FROM dispatch.""TripItems""
                         WHERE ""PickupCode"" IS NOT NULL
                           AND ""DropCode"" IS NOT NULL
                         ORDER BY ""TripId"") p
                 WHERE p.""TripId"" = t.""Id""
                   AND t.""PickupLocationCode"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickupLocationCode",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DropLocationCode",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
