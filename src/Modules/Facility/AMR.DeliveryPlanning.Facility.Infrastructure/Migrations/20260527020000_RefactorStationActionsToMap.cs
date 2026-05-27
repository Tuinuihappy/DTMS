using AMR.DeliveryPlanning.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260527020000_RefactorStationActionsToMap")]
    public partial class RefactorStationActionsToMap : Migration
    {
        // Replaces the single-action triple (ActionType / ActionCategory /
        // ActionParameters) introduced earlier today with a single jsonb map
        // Actions { intent → { actionType, category, parameters } }. The new
        // shape lets one Station carry multiple intents (e.g. a DOCK serving
        // both pickup and dropoff with separate "lift" and "drop" entries).
        // Safe to apply over a fresh DB or one that already ran the previous
        // migration — the old columns are dropped before the new one is added.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ActionParameters", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ActionCategory", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ActionType", schema: "facility", table: "Stations");

            migrationBuilder.AddColumn<string>(
                name: "Actions",
                schema: "facility",
                table: "Stations",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Actions", schema: "facility", table: "Stations");

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

            migrationBuilder.AddColumn<string>(
                name: "ActionParameters",
                schema: "facility",
                table: "Stations",
                type: "jsonb",
                nullable: true);
        }
    }
}
