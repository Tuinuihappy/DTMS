using System;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Bind each Item to the Trip that dispatched it. Enables item-level
    /// state derivation: a Trip's terminal webhook updates only THIS
    /// trip's items, so multi-group orders no longer finalize prematurely
    /// off the first completed trip. AttemptNumber mirrors
    /// Trip.AttemptNumber for per-item audit without a join.
    ///
    /// Existing rows: TripId stays NULL until the next dispatch event.
    /// Consumers fall back to the (PickupStationId, DropStationId) match
    /// for those rows so historical data is not stranded.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260605050000_AddItemTripBinding")]
    public partial class AddItemTripBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TripId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                schema: "deliveryorder",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_TripId",
                schema: "deliveryorder",
                table: "Items",
                column: "TripId",
                filter: "\"TripId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_TripId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(name: "AttemptNumber", schema: "deliveryorder", table: "Items");
            migrationBuilder.DropColumn(name: "TripId",        schema: "deliveryorder", table: "Items");
        }
    }
}
