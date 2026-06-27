using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260604160000_AddEnvelopeFieldsToTrip")]
    public partial class AddEnvelopeFieldsToTrip : Migration
    {
        // Phase (b5) of envelope-dispatch refactor: webhook → Trip lookup
        // by DTMS-side correlation key. UpperKey is the RIOT3 upperKey
        // (composite of DeliveryOrderId + group index); VendorOrderKey is
        // what RIOT3 assigned. Both nullable so legacy Job/Task trips are
        // unaffected. Unique-filtered index lets us look up by UpperKey
        // without colliding on legacy rows.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UpperKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorOrderKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_UpperKey",
                schema: "dispatch",
                table: "Trips",
                column: "UpperKey",
                unique: true,
                filter: "\"UpperKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_UpperKey",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VendorOrderKey",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "UpperKey",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
