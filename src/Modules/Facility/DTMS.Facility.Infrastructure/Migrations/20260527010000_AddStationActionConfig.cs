using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260527010000_AddStationActionConfig")]
    public partial class AddStationActionConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                schema: "facility",
                table: "Stations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionCategory",
                schema: "facility",
                table: "Stations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // jsonb gives us indexable / queryable parameters without needing
            // a separate StationActionParameters table; the current scale (one
            // small map of strings per station) doesn't justify normalization.
            migrationBuilder.AddColumn<string>(
                name: "ActionParameters",
                schema: "facility",
                table: "Stations",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ActionParameters", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ActionCategory", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ActionType", schema: "facility", table: "Stations");
        }
    }
}
