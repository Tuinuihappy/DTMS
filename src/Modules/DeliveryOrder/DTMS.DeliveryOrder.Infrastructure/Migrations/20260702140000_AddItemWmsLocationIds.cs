using System;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-2 — extend Items with WMS location snapshot columns.
    /// PURPOSE: Manual/Fleet transport-mode orders resolve
    ///   PickupLocationCode → wms.Locations.Id instead of the internal
    ///   Warehouse rows. Nullable so existing orders (with warehouse Ids
    ///   populated) stay valid + AMR orders (which leave both NULL and
    ///   populate PickupStationId) aren't affected.
    /// DEPENDS ON: prior DeliveryOrder migrations; WMS PR-1 wms.Locations
    ///   table (referenced logically — no FK because it's cross-context).
    /// REVERSIBLE: Yes — Down() drops the two new columns cleanly.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260702140000_AddItemWmsLocationIds")]
    public partial class AddItemWmsLocationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PickupWmsLocationId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DropWmsLocationId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);

            // Partial indexes — only Items with WMS location Ids matter for
            // Manual/Fleet routing lookups. Keeps the index small on an
            // AMR-heavy table.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Items_PickupWmsLocationId\" " +
                "ON deliveryorder.\"Items\" (\"PickupWmsLocationId\") " +
                "WHERE \"PickupWmsLocationId\" IS NOT NULL;");
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Items_DropWmsLocationId\" " +
                "ON deliveryorder.\"Items\" (\"DropWmsLocationId\") " +
                "WHERE \"DropWmsLocationId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS deliveryorder.\"IX_Items_PickupWmsLocationId\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS deliveryorder.\"IX_Items_DropWmsLocationId\";");

            migrationBuilder.DropColumn(
                name: "PickupWmsLocationId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DropWmsLocationId",
                schema: "deliveryorder",
                table: "Items");
        }
    }
}
