using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <summary>Phase O4 — add W3C traceparent column to <c>fleet.OutboxMessages</c>.</summary>
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260701040004_AddOutboxTraceParent")]
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "fleet",
                table: "OutboxMessages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                schema: "fleet",
                table: "OutboxMessages");
        }
    }
}
