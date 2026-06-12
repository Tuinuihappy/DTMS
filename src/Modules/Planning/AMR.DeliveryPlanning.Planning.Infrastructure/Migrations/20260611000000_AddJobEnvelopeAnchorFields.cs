using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260611000000_AddJobEnvelopeAnchorFields")]
    public partial class AddJobEnvelopeAnchorFields : Migration
    {
        // Phase b8 — Job acts as a 1:1 anchor per station-pair group, created
        // between MarkPlanning and MarkPlanned. New fields:
        //   TripId          → the dispatch.Trips row this Job spawned
        //   VendorOrderKey  → RIOT3 orderKey echoed back by SendAsync
        //   FailureReason   → why MarkFailed was called (5 categories)
        //   AttemptNumber   → 1-based, increments on Retry()

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                schema: "planning",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "DropStationId",
                schema: "planning",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                schema: "planning",
                table: "Jobs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupIndex",
                schema: "planning",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PickupStationId",
                schema: "planning",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TripId",
                schema: "planning",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorOrderKey",
                schema: "planning",
                table: "Jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_TripId",
                schema: "planning",
                table: "Jobs",
                column: "TripId",
                filter: "\"TripId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_TripId",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DropStationId",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "GroupIndex",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PickupStationId",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "TripId",
                schema: "planning",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "VendorOrderKey",
                schema: "planning",
                table: "Jobs");
        }
    }
}
