using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShelvesToFacility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shelves",
                schema: "facility",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rfid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrentStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaxWeightKg = table.Column<double>(type: "double precision", nullable: false),
                    MaxSlots = table.Column<int>(type: "integer", nullable: false),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Shelves",
                schema: "facility");
        }
    }
}
