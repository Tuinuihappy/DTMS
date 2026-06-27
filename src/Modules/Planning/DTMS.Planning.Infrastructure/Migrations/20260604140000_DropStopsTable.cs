using System;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260604140000_DropStopsTable")]
    public partial class DropStopsTable : Migration
    {
        // Stop entity was write-only — created by Leg.AddStop but never read by
        // any business logic, never published in PlanCommittedIntegrationEvent,
        // never consumed by Dispatch. Removing it has no behavioural impact.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stops",
                schema: "planning");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate Stops table with original schema if rolled back.
            migrationBuilder.CreateTable(
                name: "Stops",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegId = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    ExpectedArrival = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stops_Legs_LegId",
                        column: x => x.LegId,
                        principalSchema: "planning",
                        principalTable: "Legs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stops_LegId",
                schema: "planning",
                table: "Stops",
                column: "LegId");
        }
    }
}
