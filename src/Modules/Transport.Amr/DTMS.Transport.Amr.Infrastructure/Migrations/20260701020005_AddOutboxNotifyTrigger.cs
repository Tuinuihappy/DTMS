using DTMS.SharedKernel.Outbox;
using DTMS.Transport.Amr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Amr.Infrastructure.Migrations
{
    /// <summary>
    /// Phase O2 — install AFTER-INSERT trigger on
    /// <c>vendoradapter.OutboxMessages</c> so
    /// <see cref="DTMS.Api.Infrastructure.Outbox.OutboxListenerService"/>
    /// wakes the processor within ~50ms of a commit.
    /// </summary>
    [DbContext(typeof(VendorAdapterDbContext))]
    [Migration("20260701020005_AddOutboxNotifyTrigger")]
    public partial class AddOutboxNotifyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.InstallTriggerSql(VendorAdapterDbContext.Schema));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OutboxNotificationChannel.UninstallTriggerSql(VendorAdapterDbContext.Schema));
        }
    }
}
