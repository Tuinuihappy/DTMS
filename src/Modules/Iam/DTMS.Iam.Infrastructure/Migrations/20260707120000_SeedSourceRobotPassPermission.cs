using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Seeds the source-system robot-PASS permission code
    /// (<c>dtms:source:{key}:trip:acknowledge-robot-pass</c>) for every
    /// SystemClient that already holds the trip-lifecycle permissions — oms,
    /// sap, erp, manual. Without this row a partner presenting a valid JWT to
    /// <c>POST /api/v1/source/trips/{id}/acknowledge-robot-pass</c> would 403,
    /// because <see cref="DTMS.Iam.Application.Authorization.SourceSystemPermissionHandler"/>
    /// matches the resolved <c>dtms:source:{key}:trip:acknowledge-robot-pass</c>
    /// code against the stamped permission claims.
    ///
    /// <para>Mirrors <c>StandardSystemPermissions.TripAcknowledgeRobotPassTemplate</c>
    /// and the earlier <c>SeedSourceSystemTripPermissions</c> migration. Keys are
    /// derived by JOINing the live SystemClients table (FK-safe — a client absent
    /// in a given environment is simply skipped, and later-onboarded clients pick
    /// the code up via the StandardSystemPermissions.All auto-seed).</para>
    ///
    /// <para>Insert is idempotent (<c>ON CONFLICT DO NOTHING</c>); Down removes
    /// only rows this migration seeded (<c>GrantedBy = 'migration-robot-pass'</c>).</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260707120000_SeedSourceRobotPassPermission")]
    public partial class SeedSourceRobotPassPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemClientPermissions""
                  (""SystemKey"", ""PermissionCode"", ""GrantedAt"", ""GrantedBy"")
                SELECT sc.""Key"", 'dtms:source:' || sc.""Key"" || ':trip:acknowledge-robot-pass', now(), 'migration-robot-pass'
                FROM iam.""SystemClients"" sc
                WHERE sc.""Key"" IN ('oms', 'sap', 'erp', 'manual')
                ON CONFLICT (""SystemKey"", ""PermissionCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClientPermissions""
                 WHERE ""GrantedBy"" = 'migration-robot-pass'
                   AND ""PermissionCode"" LIKE 'dtms:source:%:trip:acknowledge-robot-pass';
            ");
        }
    }
}
