using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShelfManifests",
                schema: "dispatch");
        }
    }
}
