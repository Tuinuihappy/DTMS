using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase O4 — add W3C traceparent column to
    /// <c>deliveryorder.OutboxMessages</c>. Captured at write time from
    /// <c>Activity.Current?.Id</c>, restored by
    /// <c>OutboxProcessorService.PublishBatchAsync</c> so the consumer's
    /// span chains under the original request. Nullable — legacy rows
    /// stay valid (they start as rootless spans).
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260701040001_AddOutboxTraceParent")]
    public partial class AddOutboxTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "deliveryorder",
                table: "OutboxMessages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                schema: "deliveryorder",
                table: "OutboxMessages");
        }
    }
}
