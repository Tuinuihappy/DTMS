using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Captures WHO performed a source-system trip action and WHEN, uniformly
    /// across all four federated <c>POST /api/v1/source/trips/{id}/*</c>
    /// endpoints (acknowledge / pickup / drop / complete). Each action already
    /// writes exactly one <c>ExecutionEvent</c> row; these columns hang the
    /// actor + upstream action time off that row rather than denormalising per
    /// action onto Trip.
    ///
    /// Adds to <c>dispatch."ExecutionEvents"</c>:
    ///   • <c>Actor</c> — free-text source-system identifier of the human who
    ///     performed the action (NULL for AMR webhooks / internal events).
    ///   • <c>ActedAt</c> — when that human acted upstream. Distinct from the
    ///     existing <c>OccurredAt</c>, which stays the DTMS receive time.
    ///
    /// REVERSIBLE: Yes — Down() drops both columns.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260706131000_AddExecutionEventActor")]
    public partial class AddExecutionEventActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Actor",
                schema: "dispatch",
                table: "ExecutionEvents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActedAt",
                schema: "dispatch",
                table: "ExecutionEvents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActedAt",
                schema: "dispatch",
                table: "ExecutionEvents");

            migrationBuilder.DropColumn(
                name: "Actor",
                schema: "dispatch",
                table: "ExecutionEvents");
        }
    }
}
