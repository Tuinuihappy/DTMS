using DTMS.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    /// <summary>
    /// Phase S.3.1b — outbox-side half of the config-driven callback path.
    /// Adds <c>CorrelationId</c> + a partial unique index on
    /// <c>(PartitionKey, CorrelationId)</c> so a consumer retry that
    /// re-emits the same integration event can't double-insert outbox
    /// rows for the same callback.
    ///
    /// <para>Both columns nullable, and the index is partial — legacy
    /// rows produced by <c>DomainEventOutboxSaveChangesInterceptor</c>
    /// (PartitionKey null, CorrelationId null) are completely untouched
    /// by this constraint.</para>
    ///
    /// <para>Paired with iam migration 20260629040000 which adds
    /// <c>SystemEventSubscriptions</c>; the two together complete the
    /// fan-out producer schema.</para>
    /// </summary>
    [DbContext(typeof(OutboxDbContext))]
    [Migration("20260629030000_AddOutboxCorrelationId")]
    public partial class AddOutboxCorrelationId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.Guid>(
                name: "CorrelationId",
                schema: "outbox",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_Partition_Correlation""
                  ON outbox.""OutboxMessages"" (""PartitionKey"", ""CorrelationId"")
                  WHERE ""PartitionKey"" IS NOT NULL AND ""CorrelationId"" IS NOT NULL;
            ", suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS outbox.""IX_OutboxMessages_Partition_Correlation"";",
                suppressTransaction: true);

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "outbox",
                table: "OutboxMessages");
        }
    }
}
