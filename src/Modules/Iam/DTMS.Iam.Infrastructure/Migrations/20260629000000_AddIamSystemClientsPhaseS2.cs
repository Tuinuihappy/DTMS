using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase S.2 — federated source-system integration. Adds the
    /// schema needed to identify external callers (OMS, SAP, ERP, …)
    /// as first-class principals, store their credentials independently
    /// from user roles, and capture per-request audit logs.
    ///
    /// Tables added (all under the existing iam schema):
    /// • SystemClients              — identity row keyed by short slug
    /// • SystemClientPermissions    — what each system is authorised to do
    /// • SystemCredentials          — inbound auth config + outbound callback config (jsonb)
    /// • SystemRequestLog           — partitioned-by-month request audit
    ///
    /// Also seeds the 5 new permissions S.2 endpoints will guard, and
    /// pre-creates the current + next 2 months of request-log partitions
    /// (PartitionMaintenanceService takes over rolling them from there).
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260629000000_AddIamSystemClientsPhaseS2")]
    public partial class AddIamSystemClientsPhaseS2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── SystemClients ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "SystemClients",
                schema: "iam",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    OwnerContact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemClients", x => x.Key);
                });

            // ── SystemClientPermissions ─────────────────────────────────
            migrationBuilder.CreateTable(
                name: "SystemClientPermissions",
                schema: "iam",
                columns: table => new
                {
                    SystemKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PermissionCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    GrantedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemClientPermissions", x => new { x.SystemKey, x.PermissionCode });
                    table.ForeignKey(
                        name: "FK_SystemClientPermissions_SystemClients",
                        column: x => x.SystemKey,
                        principalSchema: "iam",
                        principalTable: "SystemClients",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemClientPermissions_SystemKey",
                schema: "iam",
                table: "SystemClientPermissions",
                column: "SystemKey");

            // ── SystemCredentials ───────────────────────────────────────
            // One row per system. AuthConfig + CallbackAuthConfig are jsonb
            // so each scheme (api-key, bearer-jwt, hmac) stores its own
            // shape without a migration per scheme. Callback fields are
            // nullable for inbound-only systems.
            migrationBuilder.CreateTable(
                name: "SystemCredentials",
                schema: "iam",
                columns: table => new
                {
                    SystemKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AuthScheme = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AuthConfig = table.Column<string>(type: "jsonb", nullable: false),
                    CallbackBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CallbackAuthScheme = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CallbackAuthConfig = table.Column<string>(type: "jsonb", nullable: true),
                    CallbackTimeoutMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 10000),
                    RetryMaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    CircuitFailureThreshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    CircuitDurationSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    UpdatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCredentials", x => x.SystemKey);
                    table.ForeignKey(
                        name: "FK_SystemCredentials_SystemClients",
                        column: x => x.SystemKey,
                        principalSchema: "iam",
                        principalTable: "SystemClients",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── SystemRequestLog (PARTITION BY RANGE OccurredAt) ────────
            // Raw SQL because EF Core has no first-class partitioned-table
            // support. PartitionMaintenanceService (S.0 §4) rolls
            // monthly partitions from here; we pre-create 3 months so a
            // service outage doesn't immediately block INSERTs.
            migrationBuilder.Sql(@"
                CREATE TABLE iam.""SystemRequestLog"" (
                    ""Id""             uuid                       NOT NULL,
                    ""OccurredAt""     timestamp with time zone   NOT NULL,
                    ""SystemKey""      character varying(50)      NOT NULL,
                    ""Method""         character varying(10)      NOT NULL,
                    ""Path""           character varying(500)     NOT NULL,
                    ""StatusCode""     integer                    NOT NULL,
                    ""IdempotencyKey"" character varying(200),
                    ""CorrelationId""  character varying(100),
                    ""DurationMs""     integer,
                    PRIMARY KEY (""OccurredAt"", ""Id"")
                ) PARTITION BY RANGE (""OccurredAt"");

                CREATE INDEX ""IX_SystemRequestLog_SystemKey_OccurredAt""
                  ON iam.""SystemRequestLog"" (""SystemKey"", ""OccurredAt"" DESC);
            ");

            // Pre-seed current + next 2 months. The maintenance service
            // takes over rolling from here every 6h.
            var thisMonth = new System.DateTime(System.DateTime.UtcNow.Year, System.DateTime.UtcNow.Month, 1, 0, 0, 0, System.DateTimeKind.Utc);
            for (int i = 0; i <= 2; i++)
            {
                var from = thisMonth.AddMonths(i);
                var to = thisMonth.AddMonths(i + 1);
                var partName = $"SystemRequestLog_{from:yyyyMM}";
                migrationBuilder.Sql($@"
                    CREATE TABLE IF NOT EXISTS iam.""{partName}"" PARTITION OF iam.""SystemRequestLog""
                    FOR VALUES FROM ('{from:yyyy-MM-dd}') TO ('{to:yyyy-MM-dd}');
                ");
            }

            // ── Seed 5 new permissions (Phase S.2 surface area) ─────────
            // dtms:source:oms:* — guard inbound endpoints under /api/v1/source/{key}/*
            // dtms:iam:system:* — guard admin endpoints under /api/v1/iam/systems/*
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  ('dtms:source:oms:order:read',   'Read OMS source orders.',                          'Source', now()),
                  ('dtms:source:oms:order:write',  'Create or update OMS source orders.',              'Source', now()),
                  ('dtms:source:oms:order:cancel', 'Cancel OMS source orders.',                        'Source', now()),
                  ('dtms:iam:system:read',         'List or read system client configuration.',        'Iam',    now()),
                  ('dtms:iam:system:write',        'Create, update, or rotate system client config.',  'Iam',    now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // Admin role inherits everything via the dtms:* wildcard
            // already mapped in 20260627000200; no explicit mapping needed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions"" WHERE ""Code"" IN (
                  'dtms:source:oms:order:read',
                  'dtms:source:oms:order:write',
                  'dtms:source:oms:order:cancel',
                  'dtms:iam:system:read',
                  'dtms:iam:system:write'
                );
            ");

            // Drop the parent (CASCADE drops the partitions).
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS iam.""SystemRequestLog"" CASCADE;");

            migrationBuilder.DropTable(
                name: "SystemCredentials",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "SystemClientPermissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "SystemClients",
                schema: "iam");
        }
    }
}
