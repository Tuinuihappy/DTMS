using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260620000030_AddOutboxPartialIndex")]
    public partial class AddOutboxPartialIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS planning.""IX_OutboxMessages_ProcessedOnUtc"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_Pending""
                  ON planning.""OutboxMessages"" (""OccurredOnUtc"")
                  WHERE ""ProcessedOnUtc"" IS NULL;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS planning.""IX_OutboxMessages_Pending"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_OutboxMessages_ProcessedOnUtc""
                  ON planning.""OutboxMessages"" (""ProcessedOnUtc"");",
                suppressTransaction: true);
        }
    }
}
