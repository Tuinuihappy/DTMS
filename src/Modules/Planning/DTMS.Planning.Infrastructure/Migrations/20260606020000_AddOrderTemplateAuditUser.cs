using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260606020000_AddOrderTemplateAuditUser")]
    public partial class AddOrderTemplateAuditUser : Migration
    {
        // Mirrors the audit-user pair added to ActionTemplates so OrderTemplate
        // GET payload can report who authored/last-edited each entry — keeps
        // both catalog resources on the same wire shape.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "planning",
                table: "OrderTemplates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiedBy",
                schema: "planning",
                table: "OrderTemplates",
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
                table: "OrderTemplates");

            migrationBuilder.DropColumn(
                name: "ModifiedBy",
                schema: "planning",
                table: "OrderTemplates");
        }
    }
}
