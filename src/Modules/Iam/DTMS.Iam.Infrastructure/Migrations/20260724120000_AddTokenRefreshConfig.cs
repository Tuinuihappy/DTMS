using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Adds <c>SystemCredentials.TokenRefreshConfig</c> — a nullable text column
    /// holding the outbound-callback token auto-refresh config (external mint
    /// endpoint + credentials + threshold) as Data Protection ciphertext
    /// (<c>CfDJ8…</c>), same encrypt-at-rest treatment as
    /// <c>CallbackAuthConfig</c>. NULL for every existing row = no auto-refresh,
    /// so there is nothing to backfill.
    ///
    /// <para>The <c>xmin</c> optimistic-concurrency token added alongside in the
    /// model needs no schema change — it is a Postgres system column that always
    /// exists.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260724120000_AddTokenRefreshConfig")]
    public partial class AddTokenRefreshConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenRefreshConfig",
                schema: "iam",
                table: "SystemCredentials",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenRefreshConfig",
                schema: "iam",
                table: "SystemCredentials");
        }
    }
}
