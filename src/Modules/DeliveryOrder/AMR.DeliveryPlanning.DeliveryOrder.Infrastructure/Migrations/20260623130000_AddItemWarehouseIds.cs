using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: 2.5 — second multi-mode transport schema addition (per ADR-002).
    /// PURPOSE: Adds PickupWarehouseId + DropWarehouseId to delivery_order.Items.
    ///   Every order references a warehouse (building/site); AMR additionally
    ///   references a specific station inside it. Nullable for now — Phase 2.6
    ///   wires IWarehouseLookup so the resolution actually populates them
    ///   alongside the existing PickupStationId / DropStationId.
    /// DEPENDS ON: 20260620000020_AddOutboxPartialIndex (last DO migration).
    ///   Logically depends on facility.Warehouses existing (added in
    ///   20260623120000_AddWarehouseAggregate) but no FK constraint yet —
    ///   Phase 2.6 adds the FK once the lookup wiring guarantees populated values.
    /// REVERSIBLE: Yes — Down() drops both columns cleanly.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260623130000_AddItemWarehouseIds")]
    public partial class AddItemWarehouseIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickupWarehouseId",
                schema: "delivery_order",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropWarehouseId",
                schema: "delivery_order",
                table: "Items",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DropWarehouseId",
                schema: "delivery_order",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PickupWarehouseId",
                schema: "delivery_order",
                table: "Items");
        }
    }
}
