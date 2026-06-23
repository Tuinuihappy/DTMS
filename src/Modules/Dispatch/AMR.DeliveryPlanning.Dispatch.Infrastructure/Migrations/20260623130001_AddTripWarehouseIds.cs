using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: 2.5 — third multi-mode transport schema addition (per ADR-002).
    /// PURPOSE: Adds PickupWarehouseId + DropWarehouseId to dispatch.Trips so
    ///   every trip snapshots which warehouse it picks-up from and drops-off
    ///   at. Mirrors the existing PickupStationId / DropStationId snapshot
    ///   pattern but at the building level — supports Manual / Fleet which
    ///   don't have a specific station inside the warehouse.
    /// DEPENDS ON: 20260622030000_AddTripVendorPauseSource (last Dispatch
    ///   migration). Logically depends on facility.Warehouses + Items
    ///   warehouse columns but no FK enforcement yet (Phase 2.6).
    /// REVERSIBLE: Yes — Down() drops both columns cleanly.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260623130001_AddTripWarehouseIds")]
    public partial class AddTripWarehouseIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickupWarehouseId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropWarehouseId",
                schema: "dispatch",
                table: "Trips",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DropWarehouseId",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PickupWarehouseId",
                schema: "dispatch",
                table: "Trips");
        }
    }
}
