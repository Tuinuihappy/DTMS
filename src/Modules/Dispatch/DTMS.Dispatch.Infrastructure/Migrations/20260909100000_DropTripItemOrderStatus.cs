using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Drops dispatch.TripItems.OrderStatus. The projector wrote it once at
    /// bind time and nothing ever refreshed it, so the trip-items endpoint
    /// served the status the order held when its items were bound — stale on
    /// every trip that outlived its order's next transition (measured
    /// 2026-09-08 on dev: 1,742 rows, not one matching the live value).
    /// Order lifecycle now comes from the DeliveryOrder side via
    /// DeliveryOrderId, which is always current.
    ///
    /// The column carried no information the system depends on: it was
    /// display-only, and the last reader (OrderRefDto) was removed first.
    /// Historic values were dumped to
    /// artifacts/tripitems-orderstatus-backup-20260909.csv before the drop.
    ///
    /// Down() restores the column with an empty-string default rather than
    /// the original per-row values — the snapshot is unreachable once
    /// dropped, and re-deriving it from today's order status would fabricate
    /// history. Rolling back therefore gives you the shape, not the data.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260909100000_DropTripItemOrderStatus")]
    public partial class DropTripItemOrderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderStatus",
                schema: "dispatch",
                table: "TripItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderStatus",
                schema: "dispatch",
                table: "TripItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }
    }
}
