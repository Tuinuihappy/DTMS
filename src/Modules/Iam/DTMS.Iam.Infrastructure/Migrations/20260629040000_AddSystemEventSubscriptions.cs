using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase S.3.1b — iam-side half of the config-driven callback path.
    /// Adds <c>iam.SystemEventSubscriptions</c> so admin can wire
    /// "system X subscribes to event-type Y" entirely in DB. The fan-out
    /// consumer reads through
    /// <see cref="DTMS.Iam.Application.Callbacks.ISubscriptionLookup"/>
    /// (cached + Redis-invalidated) so the hot path stays in memory.
    ///
    /// <para>Pairs with outbox migration 20260629030000 which adds the
    /// CorrelationId column the fan-out producer writes for idempotency.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260629040000_AddSystemEventSubscriptions")]
    public partial class AddSystemEventSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemEventSubscriptions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    SystemKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadFormatKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEventSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemEventSubscriptions_SystemClients",
                        column: x => x.SystemKey,
                        principalSchema: "iam",
                        principalTable: "SystemClients",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.UniqueConstraint(
                        "UX_SystemEventSubscriptions_SystemKey_EventType",
                        x => new { x.SystemKey, x.EventType });
                });

            // Hot path: producer asks "who subscribes to EventType X?" —
            // partial index so disabled rows don't pollute the lookup.
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_SystemEventSubscriptions_EventType_Enabled""
                  ON iam.""SystemEventSubscriptions"" (""EventType"")
                  WHERE ""Enabled"" = true;
            ");

            // Two permissions for the subscription admin endpoints. Both
            // are already implicitly covered by dtms:iam:* (Admin role)
            // but explicit codes let ops grant subscription-only access
            // without giving away the rest of the iam surface.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  ('dtms:iam:subscription:read',  'List system event subscriptions.',          'Iam', now()),
                  ('dtms:iam:subscription:write', 'Create/update/delete system subscriptions.', 'Iam', now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions"" WHERE ""Code"" IN (
                  'dtms:iam:subscription:read',
                  'dtms:iam:subscription:write'
                );
            ");

            migrationBuilder.DropTable(
                name: "SystemEventSubscriptions",
                schema: "iam");
        }
    }
}
