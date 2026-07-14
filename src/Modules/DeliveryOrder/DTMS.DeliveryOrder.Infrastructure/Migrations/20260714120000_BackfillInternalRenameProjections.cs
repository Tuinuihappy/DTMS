using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Follow-up to <c>20260711140000_RenameManualSourceSystemToInternal</c>,
    /// which back-filled only the <c>DeliveryOrders</c> aggregate table and
    /// left the two read-side snapshots at the old <c>manual</c> slug. A raw
    /// SQL rename on the aggregate does not re-emit projections, so historical
    /// rows in the list view + BI facts kept showing <c>manual</c> while their
    /// aggregate said <c>internal</c> — a split-brain visible in the orders
    /// list, its origin filter, and BI <c>GROUP BY SourceSystem</c>.
    ///
    /// <para>Mirrors the projection back-fill that
    /// <c>20260630050000_NormalizeProjectionsSourceSystemCase</c> already does
    /// for both tables. Reversible — <c>Down</c> restores the <c>manual</c>
    /// slug on the same rows.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260714120000_BackfillInternalRenameProjections")]
    public partial class BackfillInternalRenameProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Read model (orders list). Carries both the lowercase key and a
            // display name, mirroring the aggregate's two columns.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderListView""
                SET ""SourceSystem"" = 'internal',
                    ""SourceSystemDisplayName"" = 'Internal'
                WHERE ""SourceSystem"" = 'manual';
            ");

            // BI facts. No display-name column here (BI groups on the raw key).
            migrationBuilder.Sql(@"
                UPDATE bi.""OrderFacts""
                SET ""SourceSystem"" = 'internal'
                WHERE ""SourceSystem"" = 'manual';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderListView""
                SET ""SourceSystem"" = 'manual',
                    ""SourceSystemDisplayName"" = 'Manual'
                WHERE ""SourceSystem"" = 'internal';
            ");

            migrationBuilder.Sql(@"
                UPDATE bi.""OrderFacts""
                SET ""SourceSystem"" = 'manual'
                WHERE ""SourceSystem"" = 'internal';
            ");
        }
    }
}
