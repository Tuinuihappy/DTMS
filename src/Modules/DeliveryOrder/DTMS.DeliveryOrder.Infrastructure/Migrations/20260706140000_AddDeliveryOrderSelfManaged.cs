using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the <c>SelfManaged</c> flag to delivery orders. When true, the
    /// source system (OMS/WMS/ERP) executes the physical transport itself:
    /// Planning routes to the self-managed dispatch path (auto acknowledge +
    /// auto pickup, attributed to RequestedBy) instead of RIOT3 / operator
    /// pool, and the source reports drop + complete via /api/v1/source/trips/*.
    ///
    /// Non-nullable with a server default of false so existing rows
    /// (AMR/Manual-executed) backfill safely.
    ///
    /// REVERSIBLE: Yes — Down() drops the column.
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260706140000_AddDeliveryOrderSelfManaged")]
    public partial class AddDeliveryOrderSelfManaged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SelfManaged",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelfManaged",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
