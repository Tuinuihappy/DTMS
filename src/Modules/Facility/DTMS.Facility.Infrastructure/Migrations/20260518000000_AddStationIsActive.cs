using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260518000000_AddStationIsActive")]
    public partial class AddStationIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "facility",
                table: "Stations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_MapId_IsActive",
                schema: "facility",
                table: "Stations",
                columns: new[] { "MapId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stations_MapId_IsActive",
                schema: "facility",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "facility",
                table: "Stations");
        }
    }
}
