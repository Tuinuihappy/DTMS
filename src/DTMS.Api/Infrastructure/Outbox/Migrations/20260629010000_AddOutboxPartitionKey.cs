using DTMS.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    /// <summary>
    /// Phase S.3 — shards the outbox by source system. Rows that
    /// originate from a federated source callback (e.g. notify OMS
    /// when an order is delivered) carry their <c>PartitionKey =
    /// "oms"</c>; rows from the existing user/domain pipeline leave
    /// it NULL so the legacy <c>OutboxProcessorService</c> keeps
    /// picking them up unchanged.
    ///
    /// The partial index supports the new
    /// <c>MultiPartitionOutboxProcessor</c>'s per-system worker
    /// query: "next pending row for system X, eligible for retry now"
    /// — SELECT ... WHERE PartitionKey = $1 AND ProcessedOnUtc IS NULL
    /// ORDER BY OccurredOnUtc LIMIT N FOR UPDATE SKIP LOCKED.
    /// </summary>
    [DbContext(typeof(OutboxDbContext))]
    [Migration("20260629010000_AddOutboxPartitionKey")]
    public partial class AddOutboxPartitionKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartitionKey",
                schema: "outbox",
                table: "OutboxMessages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Per-partition pending-row index. CONCURRENTLY so a live
            // service ingesting outbox rows doesn't take a long write
            // lock during deploy.
            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_OutboxMessages_PartitionPending""
                  ON outbox.""OutboxMessages"" (""PartitionKey"", ""OccurredOnUtc"")
                  WHERE ""ProcessedOnUtc"" IS NULL AND ""PartitionKey"" IS NOT NULL;",
                suppressTransaction: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS outbox.""IX_OutboxMessages_PartitionPending"";",
                suppressTransaction: true);

            migrationBuilder.DropColumn(
                name: "PartitionKey",
                schema: "outbox",
                table: "OutboxMessages");
        }
    }
}
