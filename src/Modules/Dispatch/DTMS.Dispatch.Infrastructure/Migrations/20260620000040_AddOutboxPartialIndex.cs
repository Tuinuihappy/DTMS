using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260620000040_AddOutboxPartialIndex")]
    public partial class AddOutboxPartialIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS dispatch.""IX_OutboxMessages_ProcessedOnUtc"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_Pending""
                  ON dispatch.""OutboxMessages"" (""OccurredOnUtc"")
                  WHERE ""ProcessedOnUtc"" IS NULL;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS dispatch.""IX_OutboxMessages_Pending"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_OutboxMessages_ProcessedOnUtc""
                  ON dispatch.""OutboxMessages"" (""ProcessedOnUtc"");",
                suppressTransaction: true);
        }
    }
}
