using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Rename DeliveryOrders.RequiresPod → RequiresDropPod ahead of
    /// introducing a sibling RequiresPickupPod column. Metadata-only
    /// rename in Postgres — no table rewrite, no data migration.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260610100000_RenameRequiresPodToRequiresDropPod")]
    public partial class RenameRequiresPodToRequiresDropPod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequiresPod",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "RequiresDropPod");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequiresDropPod",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                newName: "RequiresPod");
        }
    }
}
