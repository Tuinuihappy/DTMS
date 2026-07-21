using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Seeds <c>dtms:iam:system:reveal-secret</c> — the dedicated permission
    /// gating the reveal-callback-token endpoint
    /// (<c>GET /api/v1/iam/systems/{key}/callback/token</c>). A separate code
    /// (not <c>system:read</c>/<c>system:write</c>) so "view a stored secret"
    /// can be granted independently of "edit config".
    ///
    /// <para><c>dtms:*</c>-prefixed, so the existing <c>('Admin','dtms:*')</c>
    /// role grant already satisfies it — no RolePermissions change. Mirrors
    /// <c>20260708120000_SeedAdminEndpointPermissions</c>. Idempotent
    /// (<c>ON CONFLICT DO NOTHING</c>); Down removes only this code.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260721050000_SeedRevealSecretPermission")]
    public partial class SeedRevealSecretPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  ('dtms:iam:system:reveal-secret', 'Reveal a system''s stored outbound callback token', 'Iam', now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions""
                 WHERE ""Code"" = 'dtms:iam:system:reveal-secret';
            ");
        }
    }
}
