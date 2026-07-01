using DTMS.Fleet.Infrastructure.Data;
using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <summary>
    /// Phase O2 — install AFTER-INSERT trigger on
    /// <c>fleet.OutboxMessages</c> so
    /// <see cref="DTMS.Api.Infrastructure.Outbox.OutboxListenerService"/>
    /// wakes the processor within ~50ms of a commit.
    /// </summary>
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260701020004_AddOutboxNotifyTrigger")]
    public partial class AddOutboxNotifyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.InstallTriggerSql(FleetDbContext.Schema));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.UninstallTriggerSql(FleetDbContext.Schema));
        }
    }
}
