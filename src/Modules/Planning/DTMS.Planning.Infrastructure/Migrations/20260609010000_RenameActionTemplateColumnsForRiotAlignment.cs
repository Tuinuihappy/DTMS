using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260609010000_RenameActionTemplateColumnsForRiotAlignment")]
    public partial class RenameActionTemplateColumnsForRiotAlignment : Migration
    {
        // Align column names with the RIOT3 wire shape:
        //   - Old "ActionType"        (STD/ACT)              → "ActionCategory"
        //   - Old "VendorActionType"  ("standardRobotsCustom") → "ActionType"
        //
        // Order matters: rename the existing "ActionType" column first to free
        // the name, then the new column takes its place. Both renames are
        // pure metadata operations in PostgreSQL — no data movement.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ActionType",
                schema: "planning",
                table: "ActionTemplates",
                newName: "ActionCategory");

            migrationBuilder.RenameColumn(
                name: "VendorActionType",
                schema: "planning",
                table: "ActionTemplates",
                newName: "ActionType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ActionType",
                schema: "planning",
                table: "ActionTemplates",
                newName: "VendorActionType");

            migrationBuilder.RenameColumn(
                name: "ActionCategory",
                schema: "planning",
                table: "ActionTemplates",
                newName: "ActionType");
        }
    }
}
