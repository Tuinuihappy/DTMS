using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Manual.Infrastructure.Migrations
{
    /// <summary>
    /// WMS PR-4b follow-up (2026-07-03) — Drop
    /// <c>ManualTripExtensions.AckDeadline</c>. The pool claim model
    /// collapses ack + assign into one atomic action (ADR-011), so a
    /// separate ack window is meaningless.
    ///
    /// REVERSIBLE: Yes — Down() re-adds the nullable column.
    /// </summary>
    [DbContext(typeof(TransportManualDbContext))]
    [Migration("20260703140000_DropManualTripExtensionAckDeadline")]
    public partial class DropManualTripExtensionAckDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AckDeadline",
                schema: "transportmanual",
                table: "ManualTripExtensions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.DateTime>(
                name: "AckDeadline",
                schema: "transportmanual",
                table: "ManualTripExtensions",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
