using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-3b — remove the deprecated warehouse Id columns from
    ///   <c>dispatch.Trips</c>. Post-PR-3 Manual dispatch persists the
    ///   trip with <c>PickupWmsLocationId</c> / <c>DropWmsLocationId</c>;
    ///   AMR leaves both pairs NULL (station-based).
    /// REVERSIBLE: Down() re-adds the columns as nullable Guid (empty).
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260702161000_DropTripWarehouseIds")]
    public partial class DropTripWarehouseIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickupWarehouseId",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DropWarehouseId",
                schema: "dispatch",
                table: "Trips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.Guid>(
                name: "PickupWarehouseId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<System.Guid>(
                name: "DropWarehouseId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);
        }
    }
}
