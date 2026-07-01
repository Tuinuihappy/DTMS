using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P1 of SourceSystem enum→dynamic-key migration. Seeds three
    /// <c>iam.SystemClients</c> rows to match every value the closed
    /// <c>SourceSystem</c> enum previously supported:
    /// <list type="bullet">
    ///   <item><c>manual</c> — internal marker for the DTMS UI / operator
    ///   flow. <b>No SystemCredential is seeded</b> because there is no
    ///   external caller; the UI-side handler resolves this key from
    ///   <see cref="DTMS.Iam.Application.Authorization.CachedSystemClientReader"/>
    ///   without ever going through <see cref="DTMS.Iam.Application.Authorization.CachedCredentialReader"/>.</item>
    ///   <item><c>sap</c> — placeholder for SAP integration. No credential
    ///   pre-seeded — admin issues client_secret via the admin UI once the
    ///   partner is ready to connect, so there is no dead secret sitting
    ///   in the DB from day 1.</item>
    ///   <item><c>erp</c> — same treatment as sap.</item>
    /// </list>
    ///
    /// <para>Permission rows are seeded for all three because the URL
    /// permission resolver (<see cref="DTMS.Iam.Application.Authorization.SourceSystemPermissionHandler"/>)
    /// pins on <c>dtms:source:{key}:order:*</c> — without the rows a
    /// partner with a valid JWT would 403. Same shape as the S.3.1a OMS
    /// seed migration.</para>
    ///
    /// <para>All inserts are idempotent (<c>ON CONFLICT DO NOTHING</c>)
    /// — ops-edited rows are preserved verbatim.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260630020000_SeedManualSapErpSystemClients")]
    public partial class SeedManualSapErpSystemClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemClients""
                  (""Key"", ""DisplayName"", ""Description"", ""IsActive"", ""OwnerContact"", ""CreatedAt"")
                VALUES
                  ('manual', 'Manual (DTMS UI)', 'Internal marker for orders created via the DTMS operator UI. Not an external caller — no SystemCredential.', true, 'internal', now()),
                  ('sap',    'SAP',              'SAP integration slot — auto-seeded by SourceSystem migration P1; admin must issue client_secret before use.', true, 'ops-please-review@dtms.local', now()),
                  ('erp',    'ERP',              'ERP integration slot — auto-seeded by SourceSystem migration P1; admin must issue client_secret before use.', true, 'ops-please-review@dtms.local', now())
                ON CONFLICT (""Key"") DO NOTHING;
            ");

            // Permission codes mirror StandardSystemPermissions.OrderWriteTemplate /
            // OrderReadTemplate resolved against each systemKey. 'manual' gets
            // BOTH codes too — even though the UI path doesn't go through the
            // URL permission resolver, keeping the rows means an admin can
            // grant an operator-role user a manual-flavored permission later
            // without a migration.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""SystemClientPermissions""
                  (""SystemKey"", ""PermissionCode"", ""GrantedAt"", ""GrantedBy"")
                VALUES
                  ('manual', 'dtms:source:manual:order:write', now(), 'migration-p1'),
                  ('manual', 'dtms:source:manual:order:read',  now(), 'migration-p1'),
                  ('sap',    'dtms:source:sap:order:write',    now(), 'migration-p1'),
                  ('sap',    'dtms:source:sap:order:read',     now(), 'migration-p1'),
                  ('erp',    'dtms:source:erp:order:write',    now(), 'migration-p1'),
                  ('erp',    'dtms:source:erp:order:read',     now(), 'migration-p1')
                ON CONFLICT (""SystemKey"", ""PermissionCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only remove the rows this migration could have inserted —
            // GrantedBy = 'migration-p1' distinguishes migration-seeded
            // rows from admin-edited ones (any manual grant carries a
            // different GrantedBy and is preserved).
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClientPermissions""
                 WHERE ""SystemKey"" IN ('manual', 'sap', 'erp')
                   AND ""GrantedBy"" = 'migration-p1';
            ");

            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClients""
                 WHERE ""Key"" IN ('sap', 'erp')
                   AND ""OwnerContact"" = 'ops-please-review@dtms.local';
            ");

            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClients""
                 WHERE ""Key"" = 'manual'
                   AND ""OwnerContact"" = 'internal';
            ");
        }
    }
}
