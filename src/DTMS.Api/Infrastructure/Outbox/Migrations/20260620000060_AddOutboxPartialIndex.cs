using DTMS.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    [DbContext(typeof(OutboxDbContext))]
    [Migration("20260620000060_AddOutboxPartialIndex")]
    public partial class AddOutboxPartialIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS outbox.""IX_OutboxMessages_ProcessedOnUtc"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_Pending""
                  ON outbox.""OutboxMessages"" (""OccurredOnUtc"")
                  WHERE ""ProcessedOnUtc"" IS NULL;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS outbox.""IX_OutboxMessages_Pending"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_OutboxMessages_ProcessedOnUtc""
                  ON outbox.""OutboxMessages"" (""ProcessedOnUtc"");",
                suppressTransaction: true);
        }
    }
}
