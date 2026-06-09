using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260609000000_AddActionTemplateVendorActionType")]
    public partial class AddActionTemplateVendorActionType : Migration
    {
        // Stores the literal RIOT3 `actionType` wire string per template
        // (e.g. "standardRobotsCustom"). Was previously derived from the
        // local ActionType enum (Std/Act) which emitted "STD"/"ACT" — that
        // is not what RIOT3 actually expects.
        //
        // Default backfills existing rows so they round-trip to the
        // correct wire value without operator intervention.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorActionType",
                schema: "planning",
                table: "ActionTemplates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "standardRobotsCustom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VendorActionType",
                schema: "planning",
                table: "ActionTemplates");
        }
    }
}
