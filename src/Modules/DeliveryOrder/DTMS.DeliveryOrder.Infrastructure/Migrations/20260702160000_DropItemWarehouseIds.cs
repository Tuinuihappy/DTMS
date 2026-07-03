using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: WMS PR-3b — remove the deprecated warehouse Id columns from
    ///   <c>deliveryorder.Items</c>. Post-PR-2 rollout Manual/Fleet items
    ///   route via <c>PickupWmsLocationId</c> / <c>DropWmsLocationId</c>;
    ///   the warehouse columns have been NULL on every order since PR-2
    ///   shipped (pre-launch, so no legacy data preservation concern).
    /// REVERSIBLE: Down() re-adds the columns as nullable Guid (empty
    ///   values); no attempt to rehydrate the deleted data.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260702160000_DropItemWarehouseIds")]
    public partial class DropItemWarehouseIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PickupWarehouseId",
                schema: "deliveryorder",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DropWarehouseId",
                schema: "deliveryorder",
                table: "Items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.Guid>(
                name: "PickupWarehouseId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<System.Guid>(
                name: "DropWarehouseId",
                schema: "deliveryorder",
                table: "Items",
                type: "uuid",
                nullable: true);
        }
    }
}
