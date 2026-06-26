using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P4.5 — seed OrderListView rows for any DeliveryOrder that
    /// doesn't yet have a projection row. Heals two cases at once:
    ///   1. Pre-existing Draft / Submitted / Validated orders (no projection
    ///      row was ever created — the old projector only fired on Confirmed).
    ///   2. Post-Confirmed orders that missed the original projection run
    ///      (e.g. broker outage during Phase P4 rollout).
    /// SearchText reuses the same concatenation as the live projector:
    ///   OrderId-as-hex + OrderRef + every ItemId.
    /// Idempotent — uses NOT EXISTS so re-running is a no-op.
    /// </summary>
    public partial class BackfillOrderListViewPreConfirmed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO deliveryorder.""OrderListView"" (
                    ""OrderId"", ""OrderRef"", ""Status"", ""SourceSystem"",
                    ""Priority"", ""TransportMode"",
                    ""HasFailedTrip"", ""HasActiveJob"", ""LatestTripId"", ""LatestJobStatus"",
                    ""RequestedBy"", ""CreatedBy"", ""Notes"",
                    ""TotalItems"", ""TotalQuantity"", ""TotalWeightKg"",
                    ""RequiresDropPod"", ""RequiresPickupPod"",
                    ""CreatedAt"", ""UpdatedAt"", ""SubmittedAt"",
                    ""ServiceWindowEarliestUtc"", ""ServiceWindowLatestUtc"",
                    ""SearchText""
                )
                SELECT
                    o.""Id"", o.""OrderRef"", o.""Status"", o.""SourceSystem"",
                    o.""Priority"", o.""RequestedTransportMode"",
                    false, false, NULL, NULL,
                    o.""RequestedBy"", o.""CreatedBy"", o.""Notes"",
                    o.""TotalItems"", o.""TotalQuantity"", o.""TotalWeightKg"",
                    o.""RequiresDropPod"", o.""RequiresPickupPod"",
                    o.""CreatedDate"", o.""UpdatedDate"", o.""SubmittedAt"",
                    o.""ServiceWindow_EarliestUtc"", o.""ServiceWindow_LatestUtc"",
                    LOWER(
                        REPLACE(o.""Id""::text, '-', '') || ' ' ||
                        COALESCE(o.""OrderRef"", '') || ' ' ||
                        COALESCE((
                            SELECT STRING_AGG(i.""ItemId"", ' ')
                            FROM deliveryorder.""Items"" i
                            WHERE i.""DeliveryOrderId"" = o.""Id""
                        ), '')
                    )
                FROM deliveryorder.""DeliveryOrders"" o
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM deliveryorder.""OrderListView"" v
                    WHERE v.""OrderId"" = o.""Id""
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. Projection rebuild from events is the canonical undo —
            // a SQL DELETE here would also wipe rows created via the normal
            // projection path, which is not what an operator would expect
            // from "reverse the last migration".
        }
    }
}
