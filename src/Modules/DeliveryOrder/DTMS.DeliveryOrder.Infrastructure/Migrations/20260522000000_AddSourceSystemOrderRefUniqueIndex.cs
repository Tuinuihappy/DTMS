using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260522000000_AddSourceSystemOrderRefUniqueIndex")]
    public partial class AddSourceSystemOrderRefUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_SourceSystem_OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                columns: new[] { "SourceSystem", "OrderRef" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_SourceSystem_OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders");
        }
    }
}
