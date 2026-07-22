using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Encrypt-at-rest for the outbound callback token: widens
    /// <c>SystemCredentials.CallbackAuthConfig</c> from <c>jsonb</c> to
    /// <c>text</c> because the stored value becomes Data Protection
    /// ciphertext (<c>CfDJ8…</c>), which is not valid JSON.
    ///
    /// <para>The encryption itself is NOT done here — a migration cannot
    /// reach the Data Protection key ring. Existing plaintext rows are
    /// converted by <c>EncryptCallbackConfigsAsync</c> in Program.cs
    /// (same seed slot as <c>SeedActionCatalogAsync</c>, runs in both
    /// migrator and api, idempotent via the CfDJ8-prefix check). Until it
    /// runs, plaintext rows keep working through the converter's
    /// pass-through.</para>
    ///
    /// <para>Down restores <c>jsonb</c> and only succeeds when every row
    /// is plaintext JSON again — by design: rolling back with ciphertext
    /// still in the column would corrupt it silently otherwise.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260722010000_EncryptCallbackAuthConfig")]
    public partial class EncryptCallbackAuthConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // USING clause required — Postgres won't apply the jsonb→text
            // cast implicitly in ALTER COLUMN.
            migrationBuilder.Sql(@"
                ALTER TABLE iam.""SystemCredentials""
                ALTER COLUMN ""CallbackAuthConfig"" TYPE text
                USING ""CallbackAuthConfig""::text;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE iam.""SystemCredentials""
                ALTER COLUMN ""CallbackAuthConfig"" TYPE jsonb
                USING ""CallbackAuthConfig""::jsonb;
            ");
        }
    }
}
