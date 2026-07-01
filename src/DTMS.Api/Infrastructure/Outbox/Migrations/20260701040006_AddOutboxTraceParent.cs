using DTMS.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    /// <summary>Phase O4 — add W3C traceparent column to central <c>outbox.OutboxMessages</c>.</summary>
    [DbContext(typeof(OutboxDbContext))]
    [Migration("20260701040006_AddOutboxTraceParent")]
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "outbox",
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
                schema: "outbox",
                table: "OutboxMessages");
        }
    }
}
