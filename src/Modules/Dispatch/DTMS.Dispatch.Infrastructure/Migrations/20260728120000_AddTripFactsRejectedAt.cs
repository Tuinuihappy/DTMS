using System;
using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// TripStatus.Rejected (vendor TASK_REJECTED / orderState REJECTED) gets
    /// its own lifecycle timestamp on bi.TripFacts, symmetric with the
    /// existing CompletedAt/FailedAt/CancelledAt trio. FinalStatus="Rejected"
    /// rides on the existing varchar(30) column, so the vehicle-performance
    /// report only needs this column for the rejected-at KPI slice.
    ///
    /// No backfill: TASK_REJECTED has never fired in production (0 rows) —
    /// the column starts NULL everywhere by definition.
    ///
    /// REVERSIBLE: Yes — Down() drops the column.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260728120000_AddTripFactsRejectedAt")]
    public partial class AddTripFactsRejectedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                schema: "bi",
                table: "TripFacts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedAt",
                schema: "bi",
                table: "TripFacts");
        }
    }
}
