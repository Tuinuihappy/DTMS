using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260606010000_AddActionTemplateAuditUser")]
    public partial class AddActionTemplateAuditUser : Migration
    {
        // Mirrors RIOT3 createdBy/modifiedBy on action templates so the GET
        // payload can report who authored/last-edited each entry. Nullable
        // because background work (seeders, future imports) may write
        // without a logged-in principal.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "planning",
                table: "ActionTemplates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                schema: "planning",
                table: "ActionTemplates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "planning",
                table: "ActionTemplates");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "planning",
                table: "ActionTemplates");
        }
    }
}
