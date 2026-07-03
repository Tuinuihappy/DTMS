using System;
using DTMS.Wms.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Wms.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-1 — initial WMS snapshot table.
    /// PURPOSE: Creates <c>wms.Locations</c> to cache the external WMS
    ///   catalogue polled by <see cref="Services.WmsLocationSyncService"/>.
    ///   Includes the functional UNIQUE INDEX on LOWER("LocationCode") that
    ///   EF's fluent HasIndex cannot express — required for case-insensitive
    ///   uniqueness enforcement + index-accelerated resolve.
    /// DEPENDS ON: no prior WMS migrations (first for this DbContext).
    /// REVERSIBLE: Yes — Down() drops the whole schema. Pre-launch so no
    ///   data preservation concern.
    /// </summary>
    [DbContext(typeof(WmsDbContext))]
    [Migration("20260702130000_AddWmsLocations")]
    public partial class AddWmsLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "wms");

            migrationBuilder.CreateTable(
                name: "Locations",
                schema: "wms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<int>(type: "integer", nullable: false),
                    LocationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    TypeName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsStorageLocation = table.Column<bool>(type: "boolean", nullable: false),
                    ParentLocationId = table.Column<int>(type: "integer", nullable: true),
                    ParentLocationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ParentLocationDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    ZGpsHeight = table.Column<double>(type: "double precision", nullable: true),
                    ZTolerance = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyMeters = table.Column<double>(type: "double precision", nullable: true),
                    HeightDiff = table.Column<double>(type: "double precision", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ExternalId",
                schema: "wms",
                table: "Locations",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_IsActive",
                schema: "wms",
                table: "Locations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ParentLocationCode",
                schema: "wms",
                table: "Locations",
                column: "ParentLocationCode");

            // Case-insensitive uniqueness — WMS returns codes that vary in
            // casing across API calls (e.g. "Line-STF_01" vs "line-stf_01");
            // downstream Order.PickupLocationCode matches loosely, so the
            // DB must reject case-variant duplicates during sync upsert.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Locations_LocationCode_Lower\" " +
                "ON wms.\"Locations\" (LOWER(\"LocationCode\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS wms.\"IX_Locations_LocationCode_Lower\";");

            migrationBuilder.DropTable(
                name: "Locations",
                schema: "wms");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS wms CASCADE;");
        }
    }
}
