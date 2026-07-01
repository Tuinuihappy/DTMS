using DTMS.Planning.Infrastructure.Data;
using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <summary>
    /// Phase O2 — install AFTER-INSERT trigger on
    /// <c>planning.OutboxMessages</c> so
    /// <see cref="DTMS.Api.Infrastructure.Outbox.OutboxListenerService"/>
    /// wakes the processor within ~50ms of a commit. See
    /// <see cref="OutboxNotificationChannel"/> for the SQL fragment.
    /// </summary>
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260701020002_AddOutboxNotifyTrigger")]
    public partial class AddOutboxNotifyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.InstallTriggerSql(PlanningDbContext.Schema));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.UninstallTriggerSql(PlanningDbContext.Schema));
        }
    }
}
