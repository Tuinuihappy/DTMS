using DTMS.Transport.Amr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Amr.Infrastructure.Migrations
{
    /// <summary>Phase O4 — add W3C traceparent column to <c>vendoradapter.OutboxMessages</c>.</summary>
    [DbContext(typeof(VendorAdapterDbContext))]
    [Migration("20260701040005_AddOutboxTraceParent")]
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "vendoradapter",
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
                schema: "vendoradapter",
                table: "OutboxMessages");
        }
    }
}
