using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Subscribes OMS to shipment.pickedup.v1 (2026-08) — the pickup-lifecycle
    /// callback POSTed to /integrations/tms/shipments/{id}/pickup-arrived.
    ///
    /// <para>Enabled=TRUE: unlike the cancelled seed, OMS's receiving endpoint
    /// is already live (confirmed via their Swagger), so the callback goes hot
    /// on deploy. The admin UI toggle remains the no-deploy off switch.</para>
    ///
    /// <para>FK-safe via INSERT..SELECT: SystemEventSubscriptions.SystemKey FKs
    /// to SystemClients.Key, and 20260711130000_ResetSeedKeepAdminOnly deletes
    /// every SystemClients row. On a DB where 'oms' was never re-created a
    /// plain VALUES insert would raise 23503 and take the migrator down; the
    /// SELECT inserts nothing instead, and an admin wires the subscription
    /// through the UI.</para>
    ///
    /// REVERSIBLE: Yes — Down() removes the seeded row.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260801150000_SeedOmsShipmentPickedUpSubscription")]
    public partial class SeedOmsShipmentPickedUpSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemEventSubscriptions""
                  (""Id"", ""SystemKey"", ""EventType"", ""PayloadFormatKey"", ""Enabled"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
                SELECT gen_random_uuid(), sc.""Key"", 'shipment.pickedup.v1', 'oms.shipment.pickedup.v1', true, now(), now()
                FROM iam.""SystemClients"" sc
                WHERE sc.""Key"" = 'oms'
                ON CONFLICT (""SystemKey"", ""EventType"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemEventSubscriptions""
                WHERE ""SystemKey"" = 'oms'
                  AND ""EventType"" = 'shipment.pickedup.v1';
            ");
        }
    }
}
