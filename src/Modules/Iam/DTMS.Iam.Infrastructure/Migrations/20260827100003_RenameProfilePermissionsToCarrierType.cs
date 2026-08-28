using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// CARRIER TRACKING P0.2b — repoints system-client grants from
    /// <c>dtms:facility:profile:*</c> to <c>dtms:fleet:carrier-type:*</c> after
    /// the carrier catalogue moved to Fleet (ADR-019).
    ///
    /// <para>The catalog itself needs no data change: it has been code-served
    /// (<c>Permissions.All</c>) since 20260801110000_DropPermissionsTable, so
    /// editing Permissions.cs is the whole story there.
    /// <c>iam.SystemClientPermissions</c> is the only table still holding raw
    /// permission codes (bare strings, no FK), which is why it needs this
    /// UPDATE.</para>
    ///
    /// <para><b>This does NOT cover user grants.</b> External Auth (ADR-014) is
    /// the sole source of truth for user permissions — the LDAP JWT carries a
    /// <c>permission</c> claim array that JwtBearer surfaces directly, and
    /// <c>PermissionClaimsTransformer</c> deliberately leaves user principals
    /// untouched. So a user holding the old granular code must be re-granted
    /// the new one on the auth service AND obtain a fresh token; the 5-minute
    /// claims cache is system-clients-only and does not help here. Holders of
    /// <c>dtms:*</c> or <c>dtms:fleet:*</c> are unaffected — see
    /// PermissionAuthorizationHandler's wildcard match.</para>
    ///
    /// PRE-FLIGHT (verified before writing): zero rows matched
    ///   'dtms:facility:profile%' or a covering wildcard in this table, so this
    ///   is expected to update 0 rows here and exists for other environments
    ///   plus any grant added by hand through the admin UI.
    /// REVERSIBLE: Down() maps the codes back.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260827100003_RenameProfilePermissionsToCarrierType")]
    public partial class RenameProfilePermissionsToCarrierType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE iam.""SystemClientPermissions""
                SET ""PermissionCode"" = 'dtms:fleet:carrier-type:read'
                WHERE ""PermissionCode"" = 'dtms:facility:profile:read';

                UPDATE iam.""SystemClientPermissions""
                SET ""PermissionCode"" = 'dtms:fleet:carrier-type:write'
                WHERE ""PermissionCode"" = 'dtms:facility:profile:write';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE iam.""SystemClientPermissions""
                SET ""PermissionCode"" = 'dtms:facility:profile:read'
                WHERE ""PermissionCode"" = 'dtms:fleet:carrier-type:read';

                UPDATE iam.""SystemClientPermissions""
                SET ""PermissionCode"" = 'dtms:facility:profile:write'
                WHERE ""PermissionCode"" = 'dtms:fleet:carrier-type:write';
            ");
        }
    }
}
