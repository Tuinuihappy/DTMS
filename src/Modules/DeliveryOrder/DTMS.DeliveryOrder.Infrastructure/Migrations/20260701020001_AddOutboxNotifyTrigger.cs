using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Phase O2 — install AFTER-INSERT trigger on
    /// <c>deliveryorder.OutboxMessages</c> that fires
    /// <c>pg_notify('outbox_notify_deliveryorder', '')</c> on commit.
    /// <see cref="DTMS.Api.Infrastructure.Outbox.OutboxListenerService"/>
    /// listens on the channel and signals the processor via
    /// <see cref="IOutboxWakeSignal"/>, cutting publish latency from
    /// ~1s (poll fallback) to ~50ms.
    ///
    /// <para>Statement-level trigger — batch INSERT of N rows fires
    /// once, not N times. AFTER-INSERT commit semantics guarantee the
    /// row is visible to the listener's next SELECT.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260701020001_AddOutboxNotifyTrigger")]
    public partial class AddOutboxNotifyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.InstallTriggerSql(DeliveryOrderDbContext.Schema));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.UninstallTriggerSql(DeliveryOrderDbContext.Schema));
        }
    }
}
