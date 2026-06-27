using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260620000020_AddOutboxPartialIndex")]
    public partial class AddOutboxPartialIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Timestamp offset (+20s vs Fleet's +0) avoids MigrationId collision in
            // the shared public.__EFMigrationsHistory table — same convention as the
            // existing AddTransactionalOutbox / AddOutboxRetryColumns batches.
            //
            // suppressTransaction:true is required because CREATE INDEX CONCURRENTLY
            // cannot run inside a transaction block. IF NOT EXISTS keeps the migration
            // idempotent across partial failures (CONCURRENTLY can leave an invalid
            // index behind).
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS deliveryorder.""IX_OutboxMessages_ProcessedOnUtc"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_Pending""
                  ON deliveryorder.""OutboxMessages"" (""OccurredOnUtc"")
                  WHERE ""ProcessedOnUtc"" IS NULL;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS deliveryorder.""IX_OutboxMessages_Pending"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_OutboxMessages_ProcessedOnUtc""
                  ON deliveryorder.""OutboxMessages"" (""ProcessedOnUtc"");",
                suppressTransaction: true);
        }
    }
}
