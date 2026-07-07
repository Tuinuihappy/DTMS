using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Captures WHO acknowledged a trip via the federated
    /// <c>POST /api/v1/source/trips/{id}/acknowledge</c> endpoint. Because that
    /// is a system-to-system call (no DTMS user), the source system forwards
    /// its own human actor in the request body and DTMS persists it here.
    ///
    /// Adds:
    ///   • <c>AcknowledgedBy</c> — free-text source-system identifier of the
    ///     human who accepted the trip (first-ack wins; every attempt still
    ///     lands in ExecutionEvent as <c>SourceAcknowledged</c>).
    ///   • <c>AcknowledgedAt</c> — when that human acked upstream.
    ///
    /// Both NULL for AMR webhook + operator-pool acks. No backfill needed.
    ///
    /// REVERSIBLE: Yes — Down() drops both columns.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260706130000_AddTripSourceAcknowledge")]
    public partial class AddTripSourceAcknowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedBy",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                schema: "dispatch",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "AcknowledgedBy",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
