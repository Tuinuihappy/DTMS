using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Subscribes OMS to shipment.cancelled.v1, and purges the rows of the two
    /// OMS formatters deleted alongside it.
    ///
    /// <para>Enabled=FALSE on purpose: OMS has not built
    /// POST /api/shipments/{shipmentId}/cancel yet. The row exists so the intent
    /// is visible in the admin UI and switching it on is a toggle, not a deploy.
    /// See docs/oms-shipment-cancel-contract.md for what OMS must confirm first —
    /// notably that a cancel may be followed by another started for the same
    /// shipment when an operator retries.</para>
    ///
    /// <para>The DELETE covers oms.shipment.cancel.v1 and oms.shipment.v1, whose
    /// formatter classes are gone as of this change. Both addressed shipments by
    /// DeliveryOrderId — an id OMS never receives — and set no RelativePath, so
    /// they POSTed to /events, which OMS does not expose. Neither was ever
    /// subscribed here, but an enabled row elsewhere would turn a quiet dead path
    /// into a dead-letter loop the moment GetRequiredKeyedService can't resolve
    /// the key.</para>
    ///
    /// <para>FK-safe via INSERT..SELECT: SystemEventSubscriptions.SystemKey FKs to
    /// SystemClients.Key, and 20260711130000_ResetSeedKeepAdminOnly (which runs
    /// BEFORE this one) deletes every SystemClients row. On a DB where 'oms' was
    /// never re-created a plain VALUES insert would raise 23503 and take the
    /// migrator down; the SELECT inserts nothing instead. Corollary: this seed is
    /// a dev-DB convenience — on a fresh or hand-configured DB it is a no-op and
    /// an admin wires the subscription through the UI.</para>
    ///
    /// REVERSIBLE: Partially — Down() removes the seeded row. The purged rows are
    /// not restored: they reference formatter classes that no longer exist, so
    /// re-creating them would only re-break the path.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260716120000_SeedOmsShipmentCancelledSubscription")]
    public partial class SeedOmsShipmentCancelledSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemEventSubscriptions""
                  (""Id"", ""SystemKey"", ""EventType"", ""PayloadFormatKey"", ""Enabled"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
                SELECT gen_random_uuid(), sc.""Key"", 'shipment.cancelled.v1', 'oms.shipment.cancelled.v1', false, now(), now()
                FROM iam.""SystemClients"" sc
                WHERE sc.""Key"" = 'oms'
                ON CONFLICT (""SystemKey"", ""EventType"") DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemEventSubscriptions""
                WHERE ""PayloadFormatKey"" IN ('oms.shipment.cancel.v1', 'oms.shipment.v1');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemEventSubscriptions""
                WHERE ""SystemKey"" = 'oms'
                  AND ""EventType"" = 'shipment.cancelled.v1';
            ");
        }
    }
}
