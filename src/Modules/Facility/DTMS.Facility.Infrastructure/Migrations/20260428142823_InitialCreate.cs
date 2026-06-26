using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "facility");

            migrationBuilder.CreateTable(
                name: "FacilityResources",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VendorRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Maps",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Width = table.Column<double>(type: "double precision", nullable: false),
                    Height = table.Column<double>(type: "double precision", nullable: false),
                    MapData = table.Column<string>(type: "jsonb", nullable: false),
                    VendorRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Maps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouteEdges",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Distance = table.Column<double>(type: "double precision", nullable: false),
                    Cost = table.Column<double>(type: "double precision", nullable: false),
                    IsBidirectional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteEdges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CoordinateX = table.Column<double>(type: "double precision", nullable: false),
                    CoordinateY = table.Column<double>(type: "double precision", nullable: false),
                    CoordinateTheta = table.Column<double>(type: "double precision", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompatibleVehicleTypes = table.Column<string>(type: "text", nullable: false),
                    VendorRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TopologyOverlays",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PolygonJson = table.Column<string>(type: "jsonb", nullable: true),
                    AffectedStationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopologyOverlays", x => x.Id);
                });

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
                });

            migrationBuilder.CreateIndex(
                name: "IX_Maps_VendorRef",
                schema: "facility",
                table: "Maps",
                column: "VendorRef",
                unique: true,
                filter: "\"VendorRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_MapId_VendorRef",
                schema: "facility",
                table: "Stations",
                columns: new[] { "MapId", "VendorRef" },
                unique: true,
                filter: "\"VendorRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TopologyOverlays_MapId_ValidUntil",
                schema: "facility",
                table: "TopologyOverlays",
                columns: new[] { "MapId", "ValidUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacilityResources",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "Maps",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "RouteEdges",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "Stations",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "TopologyOverlays",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "Zones",
                schema: "facility");
        }
    }
}
