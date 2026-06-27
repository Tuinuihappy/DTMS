using System;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260528010000_AddActionTemplates")]
    public partial class AddActionTemplates : Migration
    {
        // First entity of the OperationTemplate hierarchy (Phase 1B).
        // Mirrors the RIOT3 /api/v4/order/action-templates payload so we can
        // sync the vendor catalog if we ever want to; storing the four
        // well-known parameter slots as columns keeps queries trivial.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionTemplates",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VendorActionId = table.Column<int>(type: "integer", nullable: false),
                    Param0 = table.Column<int>(type: "integer", nullable: false),
                    Param1 = table.Column<int>(type: "integer", nullable: false),
                    ParamStr = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionTemplates_Name_Unique",
                schema: "planning",
                table: "ActionTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionTemplates_IsActive",
                schema: "planning",
                table: "ActionTemplates",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionTemplates",
                schema: "planning");
        }
    }
}
