using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase C (multi-source) — the upstream-callback audit rows used to bake
    /// the system into the EventType string (<c>UpstreamOmsNotified</c>), so a
    /// second source system would either mint new event types per system or —
    /// worse, the pre-fix behaviour — get labelled as OMS. This moves the
    /// system into a real <c>SystemKey</c> column and makes the EventType
    /// system-neutral:
    ///
    /// <para>1. Adds nullable <c>SystemKey</c> (varchar 50) to
    /// <c>OrderAuditEvents</c> and <c>OrderActivity</c>.</para>
    ///
    /// <para>2. Renames the 11 historical labels
    /// (<c>UpstreamOms{,Arrived,Cancelled}{Notified,Rejected,NotifyFailed}</c>
    /// + <c>UpstreamOms{,Arrived}ManuallyResent</c>) to their generic forms and
    /// back-fills <c>SystemKey='oms'</c> on those rows — every historical
    /// upstream row was OMS by construction.</para>
    ///
    /// <para>3. Renames the <c>OmsNotify</c> activity category to
    /// <c>UpstreamNotify</c> (reader buckets it via the default arm either
    /// way, so this is naming hygiene, not behaviour).</para>
    ///
    /// <para>Reversible — Down drops the columns and restores the OMS-branded
    /// labels on rows whose SystemKey is 'oms'.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260716150000_AddSystemKeyAndGenericUpstreamLabels")]
    public partial class AddSystemKeyAndGenericUpstreamLabels : Migration
    {
        // old ↔ new, shared by Up and Down so the two can never drift.
        private static readonly (string Old, string New)[] LabelMap =
        {
            ("UpstreamOmsNotified",                "UpstreamNotified"),
            ("UpstreamOmsRejected",                "UpstreamRejected"),
            ("UpstreamOmsNotifyFailed",            "UpstreamNotifyFailed"),
            ("UpstreamOmsArrivedNotified",         "UpstreamArrivedNotified"),
            ("UpstreamOmsArrivedRejected",         "UpstreamArrivedRejected"),
            ("UpstreamOmsArrivedNotifyFailed",     "UpstreamArrivedNotifyFailed"),
            ("UpstreamOmsCancelledNotified",       "UpstreamCancelledNotified"),
            ("UpstreamOmsCancelledRejected",       "UpstreamCancelledRejected"),
            ("UpstreamOmsCancelledNotifyFailed",   "UpstreamCancelledNotifyFailed"),
            ("UpstreamOmsManuallyResent",          "UpstreamManuallyResent"),
            ("UpstreamOmsArrivedManuallyResent",   "UpstreamArrivedManuallyResent"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemKey", schema: "deliveryorder", table: "OrderAuditEvents",
                type: "character varying(50)", maxLength: 50, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SystemKey", schema: "deliveryorder", table: "OrderActivity",
                type: "character varying(50)", maxLength: 50, nullable: true);

            foreach (var (oldLabel, newLabel) in LabelMap)
            {
                migrationBuilder.Sql($@"
                    UPDATE deliveryorder.""OrderAuditEvents""
                    SET ""EventType"" = '{newLabel}', ""SystemKey"" = 'oms'
                    WHERE ""EventType"" = '{oldLabel}';
                ");
                migrationBuilder.Sql($@"
                    UPDATE deliveryorder.""OrderActivity""
                    SET ""EventType"" = '{newLabel}', ""SystemKey"" = 'oms'
                    WHERE ""EventType"" = '{oldLabel}';
                ");
            }

            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderActivity""
                SET ""Category"" = 'UpstreamNotify', ""SystemKey"" = COALESCE(""SystemKey"", 'oms')
                WHERE ""Category"" = 'OmsNotify';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""OrderActivity""
                SET ""Category"" = 'OmsNotify'
                WHERE ""Category"" = 'UpstreamNotify' AND ""SystemKey"" = 'oms';
            ");

            foreach (var (oldLabel, newLabel) in LabelMap)
            {
                migrationBuilder.Sql($@"
                    UPDATE deliveryorder.""OrderAuditEvents""
                    SET ""EventType"" = '{oldLabel}'
                    WHERE ""EventType"" = '{newLabel}' AND ""SystemKey"" = 'oms';
                ");
                migrationBuilder.Sql($@"
                    UPDATE deliveryorder.""OrderActivity""
                    SET ""EventType"" = '{oldLabel}'
                    WHERE ""EventType"" = '{newLabel}' AND ""SystemKey"" = 'oms';
                ");
            }

            migrationBuilder.DropColumn(name: "SystemKey", schema: "deliveryorder", table: "OrderAuditEvents");
            migrationBuilder.DropColumn(name: "SystemKey", schema: "deliveryorder", table: "OrderActivity");
        }
    }
}
