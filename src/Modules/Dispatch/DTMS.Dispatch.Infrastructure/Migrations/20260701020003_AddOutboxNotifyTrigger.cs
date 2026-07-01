using DTMS.Dispatch.Infrastructure.Data;
using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Phase O2 — install AFTER-INSERT trigger on
    /// <c>dispatch.OutboxMessages</c> so
    /// <see cref="DTMS.Api.Infrastructure.Outbox.OutboxListenerService"/>
    /// wakes the processor within ~50ms of a commit.
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260701020003_AddOutboxNotifyTrigger")]
    public partial class AddOutboxNotifyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.InstallTriggerSql(DispatchDbContext.Schema));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.UninstallTriggerSql(DispatchDbContext.Schema));
        }
    }
}
