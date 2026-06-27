using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P4.5 follow-up — heal rows the old (P4) projector materialized
    /// with literal "(unknown)" placeholders for OrderRef + SourceSystem.
    /// The Confirmed event didn't carry those fields, and the previous
    /// backfill migration only INSERTed missing rows (NOT EXISTS), so these
    /// historical rows never got the real values.
    ///
    /// Refills from the write-side aggregate and rebuilds SearchText with
    /// the real OrderRef so full-text search hits them too. Targets ONLY
    /// rows where the placeholder is still present, so re-runs are no-ops.
    /// </summary>
    public partial class HealUnknownOrderListViewRows : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderListView"" v
                SET
                    ""OrderRef""        = o.""OrderRef"",
                    ""SourceSystem""    = o.""SourceSystem"",
                    ""RequestedBy""     = COALESCE(v.""RequestedBy"",  o.""RequestedBy""),
                    ""CreatedBy""       = COALESCE(v.""CreatedBy"",    o.""CreatedBy""),
                    ""Notes""           = COALESCE(v.""Notes"",        o.""Notes""),
                    ""SubmittedAt""     = COALESCE(v.""SubmittedAt"",  o.""SubmittedAt""),
                    ""ServiceWindowEarliestUtc"" = COALESCE(v.""ServiceWindowEarliestUtc"", o.""ServiceWindow_EarliestUtc""),
                    ""ServiceWindowLatestUtc""   = COALESCE(v.""ServiceWindowLatestUtc"",   o.""ServiceWindow_LatestUtc""),
                    ""RequiresDropPod""    = COALESCE(v.""RequiresDropPod"",    o.""RequiresDropPod""),
                    ""RequiresPickupPod""  = COALESCE(v.""RequiresPickupPod"",  o.""RequiresPickupPod""),
                    ""TotalItems""     = o.""TotalItems"",
                    ""TotalQuantity""  = o.""TotalQuantity"",
                    ""TotalWeightKg"" = o.""TotalWeightKg"",
                    ""SearchText"" = LOWER(
                        REPLACE(o.""Id""::text, '-', '') || ' ' ||
                        COALESCE(o.""OrderRef"", '') || ' ' ||
                        COALESCE((
                            SELECT STRING_AGG(i.""ItemId"", ' ')
                            FROM deliveryorder.""Items"" i
                            WHERE i.""DeliveryOrderId"" = o.""Id""
                        ), '')
                    )
                FROM deliveryorder.""DeliveryOrders"" o
                WHERE v.""OrderId"" = o.""Id""
                  AND (v.""OrderRef"" = '(unknown)' OR v.""SourceSystem"" = '(unknown)');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. Re-installing the placeholder would erase real data;
            // canonical undo for a projection heal is to rebuild the
            // projection from events, not a SQL revert.
        }
    }
}
