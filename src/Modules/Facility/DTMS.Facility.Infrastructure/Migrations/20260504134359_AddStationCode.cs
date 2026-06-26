using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "facility",
                table: "Stations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_MapId_Code",
                schema: "facility",
                table: "Stations",
                columns: new[] { "MapId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stations_MapId_Code",
                schema: "facility",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "facility",
                table: "Stations");
        }
    }
}
