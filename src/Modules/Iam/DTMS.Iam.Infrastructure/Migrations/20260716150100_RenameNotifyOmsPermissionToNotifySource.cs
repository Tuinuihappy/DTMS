using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase C (multi-source) — the resend endpoints became source-agnostic
    /// (they resolve the system from the order), so the permission slug drops
    /// its OMS branding: <c>dtms:deliveryorder:order:notify-oms</c> →
    /// <c>dtms:deliveryorder:order:notify-source</c>.
    ///
    /// <para>The catalog is code-served (<c>Permissions.All</c>) so only LIVE
    /// GRANTS need data-migration. Dev really holds one: role <c>ME</c> was
    /// hand-granted <c>notify-oms</c> after the 2026-07-11 seed reset — without
    /// this rename that role silently loses the resend button. Admin's
    /// <c>dtms:*</c> wildcard is unaffected. Idiom copied from
    /// <c>20260705120000_RenamePermissionsToCanonical</c> (PK-collision-safe,
    /// re-runnable; <c>PermissionAuditLog</c> history deliberately untouched).</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260716150100_RenameNotifyOmsPermissionToNotifySource")]
    public partial class RenameNotifyOmsPermissionToNotifySource : Migration
    {
        private const string OldCode = "dtms:deliveryorder:order:notify-oms";
        private const string NewCode = "dtms:deliveryorder:order:notify-source";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(RenameSql(OldCode, NewCode));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(RenameSql(NewCode, OldCode));

        private static string RenameSql(string from, string to) => $@"
            UPDATE iam.""RolePermissions"" rp SET ""PermissionCode"" = '{to}'
              WHERE rp.""PermissionCode"" = '{from}'
                AND NOT EXISTS (SELECT 1 FROM iam.""RolePermissions"" x
                                WHERE x.""Role"" = rp.""Role"" AND x.""PermissionCode"" = '{to}');
            DELETE FROM iam.""RolePermissions"" WHERE ""PermissionCode"" = '{from}';

            UPDATE iam.""SystemClientPermissions"" sp SET ""PermissionCode"" = '{to}'
              WHERE sp.""PermissionCode"" = '{from}'
                AND NOT EXISTS (SELECT 1 FROM iam.""SystemClientPermissions"" x
                                WHERE x.""SystemKey"" = sp.""SystemKey"" AND x.""PermissionCode"" = '{to}');
            DELETE FROM iam.""SystemClientPermissions"" WHERE ""PermissionCode"" = '{from}';
        ";
    }
}
