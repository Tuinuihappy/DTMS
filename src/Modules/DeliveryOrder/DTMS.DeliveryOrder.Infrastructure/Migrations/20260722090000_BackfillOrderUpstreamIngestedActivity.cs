using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// The Phase P2 swap of <c>audit-full</c> onto the <c>OrderActivity</c>
    /// projection dropped <c>OrderUpstreamIngested</c> for orders ingested
    /// after the swap — the projector has no integration event for ingest, so
    /// the row only ever existed in <c>OrderAuditEvents</c>. The frontend's
    /// upstream-notification panel keys its visibility off that row, so
    /// upstream orders whose callbacks were still retrying showed no panel at
    /// all. The handler now mirrors the row at ingest time (direct write);
    /// this migration heals the orders created in between.
    ///
    /// <para>Idempotent two ways: reuses <c>OrderAuditEvents.Id</c> as the
    /// activity PK/EventId, and skips orders that already have an ingested
    /// activity row. <c>SystemKey</c> falls back to the aggregate's
    /// <c>SourceSystemKey</c> — audit rows written before Phase C's SystemKey
    /// column are null there. Reversible — <c>Down</c> removes only rows whose
    /// Id matches an audit row (handler-written rows use fresh Guids and are
    /// untouched).</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260722090000_BackfillOrderUpstreamIngestedActivity")]
    public partial class BackfillOrderUpstreamIngestedActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO deliveryorder.""OrderActivity""
                    (""Id"", ""EventId"", ""OrderId"", ""Category"", ""EventType"",
                     ""Details"", ""ActorId"", ""OccurredAt"", ""Channel"", ""DisplayName"", ""SystemKey"")
                SELECT a.""Id"", a.""Id"", a.""DeliveryOrderId"", 'OrderLifecycle', a.""EventType"",
                       a.""Details"", a.""ActorId"", a.""OccurredAt"", a.""Channel"", a.""DisplayName"",
                       COALESCE(a.""SystemKey"", o.""SourceSystemKey"")
                FROM deliveryorder.""OrderAuditEvents"" a
                JOIN deliveryorder.""DeliveryOrders"" o ON o.""Id"" = a.""DeliveryOrderId""
                WHERE a.""EventType"" = 'OrderUpstreamIngested'
                  AND NOT EXISTS (
                      SELECT 1 FROM deliveryorder.""OrderActivity"" x
                      WHERE x.""OrderId"" = a.""DeliveryOrderId""
                        AND x.""EventType"" = 'OrderUpstreamIngested');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM deliveryorder.""OrderActivity"" x
                USING deliveryorder.""OrderAuditEvents"" a
                WHERE x.""Id"" = a.""Id""
                  AND x.""EventType"" = 'OrderUpstreamIngested';
            ");
        }
    }
}
