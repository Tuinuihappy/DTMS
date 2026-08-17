using System;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: dead-domain removal — drops the five Facility tables whose
    /// domain code was deleted in the same effort, plus the always-null
    /// Stations.ZoneId column.
    ///
    /// <para>Why each is dead: Zones and RouteEdges never had a writer
    /// (Map.AddZone / Map.AddRouteEdge had zero call sites; RouteEdgeSyncService
    /// was deleted 2026-07-17 because RIOT3 offers no station-to-station cost
    /// API, so RouteEdges stayed empty since install). TopologyOverlays was
    /// write-only — created and expired but never read by routing or dispatch.
    /// FacilityResources had endpoints but no caller. Shelves had a release
    /// path (ShelfReleaseConsumer) but no creation path — RegisterShelfCommand
    /// was never wired to an endpoint, so the table could only be populated by
    /// hand.</para>
    ///
    /// PRE-FLIGHT (must hold before this runs): zero rows in all five tables
    ///   and Stations.ZoneId IS NULL everywhere — verified against the dev DB
    ///   as part of this change; the tables never had a code path that INSERTs.
    /// REVERSIBLE: Down() recreates the full schema of record — tables,
    ///   indexes, and the FK_{table}_Maps_MapId cascade constraints added by
    ///   20260807120000_AddMapChildForeignKeys (Shelves deliberately had no FK
    ///   there — a shelf outlives any single map version). Data is NOT
    ///   recoverable, which is acceptable because the tables are empty.
    /// </summary>
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260814000000_DropDeadFacilityEntities")]
    public partial class DropDeadFacilityEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FKs to Maps drop together with their owning tables.
            migrationBuilder.DropTable(name: "Zones", schema: "facility");
            migrationBuilder.DropTable(name: "RouteEdges", schema: "facility");
            migrationBuilder.DropTable(name: "TopologyOverlays", schema: "facility");
            migrationBuilder.DropTable(name: "FacilityResources", schema: "facility");
            migrationBuilder.DropTable(name: "Shelves", schema: "facility");

            migrationBuilder.DropColumn(
                name: "ZoneId",
                schema: "facility",
                table: "Stations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ZoneId",
                schema: "facility",
                table: "Stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Zones",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SpeedLimit = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zones_Maps_MapId",
                        column: x => x.MapId,
                        principalSchema: "facility",
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteEdges",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cost = table.Column<double>(type: "double precision", nullable: false),
                    Distance = table.Column<double>(type: "double precision", nullable: false),
                    IsBidirectional = table.Column<bool>(type: "boolean", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetStationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteEdges_Maps_MapId",
                        column: x => x.MapId,
                        principalSchema: "facility",
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TopologyOverlays",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffectedStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolygonJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopologyOverlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TopologyOverlays_Maps_MapId",
                        column: x => x.MapId,
                        principalSchema: "facility",
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TopologyOverlays_MapId_ValidUntil",
                schema: "facility",
                table: "TopologyOverlays",
                columns: new[] { "MapId", "ValidUntil" });

            migrationBuilder.CreateTable(
                name: "FacilityResources",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VendorRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityResources_Maps_MapId",
                        column: x => x.MapId,
                        principalSchema: "facility",
                        principalTable: "Maps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Shelves: no FK to Maps by design (20260807120000 excluded it —
            // a physical RFID carrier outlives any single map version).
            migrationBuilder.CreateTable(
                name: "Shelves",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxSlots = table.Column<int>(type: "integer", nullable: false),
                    MaxWeightKg = table.Column<double>(type: "double precision", nullable: false),
                    Rfid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shelves", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_MapId",
                schema: "facility",
                table: "Shelves",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_Rfid",
                schema: "facility",
                table: "Shelves",
                column: "Rfid",
                unique: true);
        }
    }
}
