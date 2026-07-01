using System;
using DTMS.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    /// <summary>
    /// Phase O3 — dedicated Dead Letter Queue table for terminally-failed
    /// outbox messages. Central schema (<c>outbox</c>) serves all modules;
    /// the <c>Source</c> column identifies which module DbContext to route
    /// a replay back to. See <c>DeadLetterMessage</c> XML docs for the
    /// design rationale.
    ///
    /// <para>Unique index on <c>OriginalOutboxId</c> makes the
    /// <c>MoveAsync</c> operation idempotent: a re-attempt after a
    /// prior partial success (DLQ insert OK, module-side delete failed)
    /// hits the constraint and no-ops.</para>
    /// </summary>
    [DbContext(typeof(OutboxDbContext))]
    [Migration("20260701030001_AddDeadLetterMessages")]
    public partial class AddDeadLetterMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeadLetterMessages",
                schema: "outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalOutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstFailedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastFailedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterMessages_OriginalOutboxId",
                schema: "outbox",
                table: "DeadLetterMessages",
                column: "OriginalOutboxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterMessages_LastFailedOnUtc",
                schema: "outbox",
                table: "DeadLetterMessages",
                column: "LastFailedOnUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterMessages_Source",
                schema: "outbox",
                table: "DeadLetterMessages",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeadLetterMessages",
                schema: "outbox");
        }
    }
}
