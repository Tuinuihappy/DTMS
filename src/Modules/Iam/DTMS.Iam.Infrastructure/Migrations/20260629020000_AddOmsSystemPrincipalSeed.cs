using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase S.3.1a — production seed for the OMS SystemClient principal
    /// plus the two standard permissions it needs to call
    /// <c>/api/v1/source/oms/delivery-orders</c>.
    ///
    /// <para>Up until S.3.1a these rows were created by hand during S.2
    /// smoke testing; the static endpoint hardcoded
    /// <c>dtms:source:oms:order:write</c> so the manually-inserted
    /// permission row carried the workflow. Switching to
    /// <see cref="DTMS.Iam.Application.Authorization.SourceSystemPermissionHandler"/>
    /// (dynamic permission per URL key) means the same rows are now load-
    /// bearing for *production* — a fresh database without them would
    /// reject every OMS callback with 403. This migration promotes them
    /// from "test fixture" to "schema seed".</para>
    ///
    /// <para>SystemClient seed metadata is intentionally minimal —
    /// DisplayName + OwnerContact carry sentinel values so ops can spot
    /// "this row was migration-seeded, please fill in real owner contact"
    /// when they review iam.SystemClients post-deploy.</para>
    ///
    /// <para>All inserts are idempotent: ON CONFLICT DO NOTHING preserves
    /// whatever ops has typed in during S.2 manual onboarding.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260629020000_AddOmsSystemPrincipalSeed")]
    public partial class AddOmsSystemPrincipalSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemClients""
                  (""Key"", ""DisplayName"", ""Description"", ""IsActive"", ""OwnerContact"", ""CreatedAt"")
                VALUES
                  ('oms', 'OMS', 'Upstream order management — auto-seeded by S.3.1a; replace OwnerContact when known.', true, 'ops-please-review@dtms.local', now())
                ON CONFLICT (""Key"") DO NOTHING;
            ");

            // Permission codes mirror StandardSystemPermissions.OrderWriteTemplate /
            // OrderReadTemplate resolved against systemKey='oms'. If those
            // templates are ever changed (new surface, e.g. order:cancel)
            // a follow-up migration must seed the new code for every
            // existing system — there is no auto-fan-out in Phase S.4 yet.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemClientPermissions""
                  (""SystemKey"", ""PermissionCode"", ""GrantedAt"", ""GrantedBy"")
                VALUES
                  ('oms', 'dtms:source:oms:order:write', now(), 'migration-s3.1a'),
                  ('oms', 'dtms:source:oms:order:read',  now(), 'migration-s3.1a')
                ON CONFLICT (""SystemKey"", ""PermissionCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only remove the rows this migration could have inserted —
            // GrantedBy column distinguishes migration-seeded rows from
            // ops-edited ones (any non-null GrantedBy preserved by the
            // ON CONFLICT). Down is best-effort: if ops edited the
            // GrantedBy field, the row stays.
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClientPermissions""
                 WHERE ""SystemKey"" = 'oms'
                   AND ""GrantedBy"" = 'migration-s3.1a';
            ");

            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClients""
                 WHERE ""Key"" = 'oms'
                   AND ""OwnerContact"" = 'ops-please-review@dtms.local';
            ");
        }
    }
}
