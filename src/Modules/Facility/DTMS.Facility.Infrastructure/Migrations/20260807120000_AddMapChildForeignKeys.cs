using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: data-integrity hardening — make orphaned map topology
    ///   structurally impossible.
    ///
    /// <para>Background: the facility schema shipped with NO foreign keys at
    /// all, so deleting a map (only ever done by hand — there is no delete-map
    /// endpoint or command) left its children behind. Prod carried 13 such
    /// orphaned Stations rows from the map swap to FAN1_MAP_06242025A, and
    /// because RIOT3 reuses its numeric station ids across map versions those
    /// orphans duplicated live VendorRefs (177 = live SHELF_FG_1 + orphaned
    /// SHELF10). Vendor-ref resolution then picked between them
    /// nondeterministically. The resolver was hardened with a Maps-liveness
    /// join and the orphan rows deleted on 2026-08-06; this migration closes
    /// the hole the data drifted through in the first place.</para>
    ///
    /// <para>Shelves is deliberately EXCLUDED: a Shelf is a physical
    /// RFID-tracked carrier that outlives any single map version, so cascading
    /// a map delete into it would destroy real asset records. Its MapId stays
    /// an unconstrained "where it currently lives" pointer.</para>
    ///
    /// <para>These constraints live at the DB level only — the EF model does
    /// not declare Map→children relationships (Map's collection navigations
    /// are Ignore()d in FacilityDbContext), so the model snapshot is
    /// intentionally left untouched. Migrations in this repo are hand-written
    /// (dotnet-ef is incompatible with the .NET 10 preview SDK), so there is
    /// no scaffolding pass that would try to revert the drift.</para>
    ///
    /// PRE-FLIGHT (verified 2026-08-07 against the dev DB): zero orphan rows
    ///   in every table below, every MapId column NOT NULL, and no existing
    ///   foreign keys in the facility schema — so each constraint validates
    ///   without a data fix-up.
    /// REVERSIBLE: Down() drops all five constraints.
    /// </summary>
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260807120000_AddMapChildForeignKeys")]
    public partial class AddMapChildForeignKeys : Migration
    {
        // Every table whose rows describe the topology of one map and are
        // meaningless once that map is gone.
        private static readonly string[] ChildTables =
        [
            "Stations",
            "Zones",
            "RouteEdges",
            "TopologyOverlays",
            "FacilityResources",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in ChildTables)
            {
                migrationBuilder.AddForeignKey(
                    name: $"FK_{table}_Maps_MapId",
                    schema: "facility",
                    table: table,
                    column: "MapId",
                    principalSchema: "facility",
                    principalTable: "Maps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in ChildTables)
            {
                migrationBuilder.DropForeignKey(
                    name: $"FK_{table}_Maps_MapId",
                    schema: "facility",
                    table: table);
            }
        }
    }
}
