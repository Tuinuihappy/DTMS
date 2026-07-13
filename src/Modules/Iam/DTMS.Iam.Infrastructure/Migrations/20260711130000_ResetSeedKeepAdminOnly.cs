using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Production hardening — strip ALL seed data (SystemClients, credentials,
    /// permission catalog, roles, subscriptions) so a fresh environment is
    /// configured entirely by hand via the admin UI. The ONLY row kept is the
    /// bootstrap: role <c>Admin</c> granted <c>dtms:*</c>, without which the
    /// first operator would log in (role from External Auth) yet resolve zero
    /// permissions and be unable to configure anything (chicken-and-egg lock).
    ///
    /// <para><b>Runs everywhere.</b> On a fresh DB the earlier seed migrations
    /// insert their rows and this one wipes them back to Admin-only; on the
    /// dev DB it clears the existing seed. Both converge to the same end
    /// state: empty auth + Admin bootstrap.</para>
    ///
    /// <para><b>Deletes are FK-safe</b> (children before parents). Cascades
    /// would cover it, but explicit ordering keeps the intent readable and
    /// works regardless of a table's cascade config.</para>
    ///
    /// <para><b>Down is a no-op.</b> Deleted seed data cannot be reconstructed;
    /// re-running the original seed migrations is the way back, not a Down.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260711130000_ResetSeedKeepAdminOnly")]
    public partial class ResetSeedKeepAdminOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- ── Wipe every seeded row (children first) ──────────────────
                DELETE FROM iam.""SystemIssuedTokens"";
                DELETE FROM iam.""SystemEventSubscriptions"";
                DELETE FROM iam.""SystemCredentials"";
                DELETE FROM iam.""SystemClientPermissions"";
                DELETE FROM iam.""SystemClients"";
                DELETE FROM iam.""RolePermissions"";
                DELETE FROM iam.""Permissions"";
                DELETE FROM iam.""Roles"";

                -- ── Bootstrap: the single identity that must exist ──────────
                -- Admin role comes from External Auth (JWT role claim); this
                -- row is what maps it to the dtms:* wildcard so the first
                -- operator can log in and configure everything else by hand.
                INSERT INTO iam.""Roles"" (""Name"", ""Description"", ""IsSystem"", ""CreatedAt"")
                VALUES ('Admin', 'Administrator (bootstrap)', TRUE, now())
                ON CONFLICT (""Name"") DO NOTHING;

                INSERT INTO iam.""RolePermissions"" (""Role"", ""PermissionCode"")
                VALUES ('Admin', 'dtms:*')
                ON CONFLICT (""Role"", ""PermissionCode"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: a data wipe cannot be un-done. To restore seed data,
            // re-apply the original seed migrations against a clean DB.
        }
    }
}
