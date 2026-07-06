using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Seeds the four source-system trip-lifecycle permission codes
    /// (<c>dtms:source:{key}:trip:acknowledge|pickup|drop|complete</c>) for
    /// every SystemClient that already holds the order permissions — oms,
    /// sap, erp, manual. Without these rows a partner presenting a valid JWT
    /// to <c>POST /api/v1/source/trips/*</c> would 403, because
    /// <see cref="DTMS.Iam.Application.Authorization.SourceSystemPermissionHandler"/>
    /// matches the resolved <c>dtms:source:{key}:trip:*</c> code against the
    /// stamped permission claims.
    ///
    /// <para>Codes mirror the new
    /// <see cref="DTMS.Iam.Application.Authorization.StandardSystemPermissions"/>
    /// trip templates. Credential-less slots (sap/erp/manual) are granted too
    /// — harmless, since auth still gates the call, and it saves a migration
    /// when a partner is later activated.</para>
    ///
    /// <para>All inserts idempotent (<c>ON CONFLICT DO NOTHING</c>); Down
    /// removes only rows this migration seeded (<c>GrantedBy = 'migration-trip'</c>).</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260706120000_SeedSourceSystemTripPermissions")]
    public partial class SeedSourceSystemTripPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Derive keys by JOINing the live SystemClients table rather than
            // a hardcoded VALUES list — a slot in the intended set (sap/erp)
            // may not exist in every environment, and inserting a permission
            // for a non-existent client violates
            // FK_SystemClientPermissions_SystemClients. SELECTing from the
            // table makes the seed FK-safe regardless of which clients are
            // present; clients onboarded later pick up the codes via the
            // StandardSystemPermissions.All auto-seed instead.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemClientPermissions""
                  (""SystemKey"", ""PermissionCode"", ""GrantedAt"", ""GrantedBy"")
                SELECT sc.""Key"", 'dtms:source:' || sc.""Key"" || p.suffix, now(), 'migration-trip'
                FROM iam.""SystemClients"" sc
                CROSS JOIN (VALUES
                    (':trip:acknowledge'),
                    (':trip:pickup'),
                    (':trip:drop'),
                    (':trip:complete')) AS p(suffix)
                WHERE sc.""Key"" IN ('oms', 'sap', 'erp', 'manual')
                ON CONFLICT (""SystemKey"", ""PermissionCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClientPermissions""
                 WHERE ""GrantedBy"" = 'migration-trip'
                   AND ""PermissionCode"" LIKE 'dtms:source:%:trip:%';
            ");
        }
    }
}
