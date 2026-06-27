using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260606000000_DropActionTemplateDescription")]
    public partial class DropActionTemplateDescription : Migration
    {
        // Description was a DTMS-only addition on top of the RIOT3
        // /api/v4/order/action-templates payload. Removing it brings the
        // payload back to a clean wire-compat shape so operators can paste
        // a RIOT3 request body straight in.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                schema: "planning",
                table: "ActionTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "planning",
                table: "ActionTemplates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
