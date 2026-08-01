using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Renames OMS's drop callback subscription in place (2026-08):
    /// shipment.arrived.v1 → shipment.droppedoff.v1 and
    /// oms.shipment.arrived.v1 → oms.shipment.droppedoff.v1, following OMS's
    /// route move to /integrations/tms/shipments/{id}/dropoff-arrived.
    ///
    /// <para>UPDATE in place (first of its kind here) rather than
    /// delete+insert: it preserves whatever Enabled state the admin has set —
    /// a delete+insert would silently re-enable a subscription an operator had
    /// turned off. Safe against UX_SystemEventSubscriptions_SystemKey_EventType
    /// (no row holds the new pair) and a no-op when the row is absent
    /// (ResetSeedKeepAdminOnly wipes the table on some DBs).</para>
    ///
    /// REVERSIBLE: Yes — Down() reverses the rename.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260801170000_RenameOmsShipmentArrivedToDroppedOff")]
    public partial class RenameOmsShipmentArrivedToDroppedOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE iam.""SystemEventSubscriptions""
                   SET ""EventType"" = 'shipment.droppedoff.v1',
                       ""PayloadFormatKey"" = 'oms.shipment.droppedoff.v1',
                       ""UpdatedAtUtc"" = now()
                 WHERE ""SystemKey"" = 'oms'
                   AND ""EventType"" = 'shipment.arrived.v1';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE iam.""SystemEventSubscriptions""
                   SET ""EventType"" = 'shipment.arrived.v1',
                       ""PayloadFormatKey"" = 'oms.shipment.arrived.v1',
                       ""UpdatedAtUtc"" = now()
                 WHERE ""SystemKey"" = 'oms'
                   AND ""EventType"" = 'shipment.droppedoff.v1';
            ");
        }
    }
}
