using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P2 of SourceSystem enum→dynamic-key migration. Two-step:
    /// <list type="number">
    ///   <item>Backfill <c>SourceSystemKey</c> + <c>SourceSystemDisplayName</c>
    ///   from the legacy enum column by joining <c>iam.SystemClients</c>
    ///   — Display snapshot is the value present in the target env at
    ///   migrate time (portable across dev/staging/prod where ops may
    ///   have edited DisplayName after seed).</item>
    ///   <item>Create <c>IX_DeliveryOrders_SourceSystemKey_OrderRef</c>
    ///   alongside the legacy <c>IX_DeliveryOrders_SourceSystem_OrderRef</c>.
    ///   Dual-window: both unique constraints enforce identical rows
    ///   through P5, and P6 drops the legacy one.</item>
    /// </list>
    ///
    /// <para><b>Depends on P1</b> having seeded the manual/sap/erp rows
    /// (S.3.1a seeded 'oms'). If any historical order references a
    /// SourceSystem enum value whose lowercase form is not in
    /// iam.SystemClients, that order's <c>SourceSystemKey</c> stays NULL
    /// — the JOIN filter silently skips it. The index creation still
    /// succeeds (NULL is not indexed for unique-constraint purposes).
    /// Ops should follow up on any such rows manually — a
    /// <c>SELECT ... WHERE "SourceSystemKey" IS NULL</c> post-migration
    /// audit query surfaces them.</para>
    ///
    /// <para><b>Rollback:</b> Down() drops the new index and NULLs the
    /// two backfilled columns. Safe — P3+ hasn't started reading from
    /// them yet.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260630030000_BackfillSourceSystemKey")]
    public partial class BackfillSourceSystemKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill via JOIN. The lower() on the left side normalizes
            // enum PascalCase ("Oms") to slug lowercase ("oms") to match
            // SystemClient.Key's slug format.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders"" AS o
                   SET ""SourceSystemKey""         = lower(o.""SourceSystem""),
                       ""SourceSystemDisplayName"" = sc.""DisplayName""
                  FROM iam.""SystemClients"" AS sc
                 WHERE lower(o.""SourceSystem"") = sc.""Key""
                   AND o.""SourceSystemKey"" IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_SourceSystemKey_OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders",
                columns: new[] { "SourceSystemKey", "OrderRef" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_SourceSystemKey_OrderRef",
                schema: "deliveryorder",
                table: "DeliveryOrders");

            // Null the backfill so a re-apply of Up() re-runs the JOIN
            // (idempotent through the "WHERE SourceSystemKey IS NULL" guard
            // in Up). Legacy enum column stays authoritative — read paths
            // haven't switched yet.
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                   SET ""SourceSystemKey"" = NULL,
                       ""SourceSystemDisplayName"" = NULL;
            ");
        }
    }
}
