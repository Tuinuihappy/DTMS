using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase S.5 (B2) — subscribes the OMS system to the new shipment callback
    /// events so the federated fan-out (ShipmentStarted/ArrivedCallbackFanout)
    /// routes them, replacing the legacy hardcoded OMS adapter.
    ///
    /// Enabled=true, but inert until the <c>Callbacks:ShipmentEventsEnabled</c>
    /// flag is flipped at cutover — the fan-out consumers return before they
    /// consult subscriptions while the flag is off. Post-cutover (Phase 4) the
    /// subscription's Enabled becomes the disable switch, uniform with erp/sap.
    ///
    /// FK-safe: SystemKey 'oms' is seeded by 20260629020000_AddOmsSystemPrincipalSeed.
    /// Idempotent (ON CONFLICT DO NOTHING on the (SystemKey, EventType) unique).
    ///
    /// REVERSIBLE: Yes — Down() deletes the two rows.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260708160000_SeedOmsShipmentSubscriptions")]
    public partial class SeedOmsShipmentSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemEventSubscriptions""
                  (""Id"", ""SystemKey"", ""EventType"", ""PayloadFormatKey"", ""Enabled"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
                VALUES
                  (gen_random_uuid(), 'oms', 'shipment.started.v1', 'oms.shipment.started.v1', true, now(), now()),
                  (gen_random_uuid(), 'oms', 'shipment.arrived.v1', 'oms.shipment.arrived.v1', true, now(), now())
                ON CONFLICT (""SystemKey"", ""EventType"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemEventSubscriptions""
                WHERE ""SystemKey"" = 'oms'
                  AND ""EventType"" IN ('shipment.started.v1', 'shipment.arrived.v1');
            ");
        }
    }
}
