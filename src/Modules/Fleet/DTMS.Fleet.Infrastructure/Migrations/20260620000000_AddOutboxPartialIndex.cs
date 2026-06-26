using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260620000000_AddOutboxPartialIndex")]
    public partial class AddOutboxPartialIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS fleet.""IX_OutboxMessages_ProcessedOnUtc"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_Pending""
                  ON fleet.""OutboxMessages"" (""OccurredOnUtc"")
                  WHERE ""ProcessedOnUtc"" IS NULL;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS fleet.""IX_OutboxMessages_Pending"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_OutboxMessages_ProcessedOnUtc""
                  ON fleet.""OutboxMessages"" (""ProcessedOnUtc"");",
                suppressTransaction: true);
        }
    }
}
