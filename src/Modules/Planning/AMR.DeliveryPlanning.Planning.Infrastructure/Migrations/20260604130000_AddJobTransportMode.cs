using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260604130000_AddJobTransportMode")]
    public partial class AddJobTransportMode : Migration
    {
        // Phase 2 of TransportMode wiring: planner now carries the requested
        // mode it received from DeliveryOrder.Confirmed onto the Job aggregate,
        // so downstream consumers (dispatch, vehicle matching) can act on it.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransportMode",
                schema: "planning",
                table: "Jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransportMode",
                schema: "planning",
                table: "Jobs");
        }
    }
}
