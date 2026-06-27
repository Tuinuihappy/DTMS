using System;
using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260604150000_AddOrderTemplateRouteFields")]
    public partial class AddOrderTemplateRouteFields : Migration
    {
        // Phase (b1) of envelope-dispatch refactor: route-based OrderTemplate
        // lookup. PickupStationId + DropStationId are nullable so existing
        // generic templates aren't forced to become route-specific.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickupStationId",
                schema: "planning",
                table: "OrderTemplates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropStationId",
                schema: "planning",
                table: "OrderTemplates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderTemplates_PickupStationId_DropStationId_IsActive",
                schema: "planning",
                table: "OrderTemplates",
                columns: new[] { "PickupStationId", "DropStationId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderTemplates_PickupStationId_DropStationId_IsActive",
                schema: "planning",
                table: "OrderTemplates");

            migrationBuilder.DropColumn(
                name: "DropStationId",
                schema: "planning",
                table: "OrderTemplates");

            migrationBuilder.DropColumn(
                name: "PickupStationId",
                schema: "planning",
                table: "OrderTemplates");
        }
    }
}
