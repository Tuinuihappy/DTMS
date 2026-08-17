using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: dead-domain removal — drops dispatch.ShelfManifests together
    /// with the Facility Shelf aggregate it existed to serve.
    ///
    /// <para>The manifest chain was dead end-to-end: nothing ever constructed
    /// a ShelfManifest (zero call sites), so ShelfReleaseConsumer — the only
    /// reader — was a permanent no-op and has been deleted along with the
    /// entity and repository.</para>
    ///
    /// PRE-FLIGHT: zero rows (no INSERT path existed) — verified against the
    ///   dev DB as part of this change.
    /// REVERSIBLE: Down() recreates the exact schema of
    ///   20260506090951_AddShelfManifest (table + both indexes); data is not
    ///   recoverable, which is acceptable because the table is empty.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260814000001_DropShelfManifests")]
    public partial class DropShelfManifests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShelfManifests",
                schema: "dispatch");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShelfManifests",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShelfRfid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PackageBarcodes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShelfManifests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShelfManifests_JobId",
                schema: "dispatch",
                table: "ShelfManifests",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShelfManifests_TripId",
                schema: "dispatch",
                table: "ShelfManifests",
                column: "TripId",
                filter: "\"TripId\" IS NOT NULL");
        }
    }
}
