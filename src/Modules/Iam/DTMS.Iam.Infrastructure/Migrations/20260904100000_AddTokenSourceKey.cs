using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Adds <c>TokenSourceKey</c> — the link that lets one system borrow another
    /// system's outbound bearer token instead of minting its own.
    ///
    /// The auth service this deployment mints against keeps only one live token
    /// per account. Two systems configured with the same mint credentials
    /// therefore invalidate each other on every refresh, and nothing detects it:
    /// the stored <c>exp</c> still reads as valid, so the refresh loop finds
    /// nothing to do while every outbound call comes back 401 until that
    /// system's own schedule comes round. Pointing several systems at one owner
    /// leaves exactly one minter and removes the race.
    ///
    /// Only the token is shared. CallbackBaseUrl, timeout and the resilience
    /// settings stay per-row, which is why this is a link rather than a merged
    /// credential.
    ///
    /// Self-referencing FK with ON DELETE RESTRICT so an owner cannot be deleted
    /// while borrowers still point at it. Nullable with no backfill: every
    /// existing row keeps owning its token, so behaviour is unchanged until an
    /// operator sets a link.
    ///
    /// REVERSIBLE: Yes — Down() drops the index, the FK and the column.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260904100000_AddTokenSourceKey")]
    public partial class AddTokenSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenSourceKey",
                schema: "iam",
                table: "SystemCredentials",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemCredentials_TokenSourceKey",
                schema: "iam",
                table: "SystemCredentials",
                column: "TokenSourceKey",
                filter: "\"TokenSourceKey\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemCredentials_SystemCredentials_TokenSourceKey",
                schema: "iam",
                table: "SystemCredentials",
                column: "TokenSourceKey",
                principalSchema: "iam",
                principalTable: "SystemCredentials",
                principalColumn: "SystemKey",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemCredentials_SystemCredentials_TokenSourceKey",
                schema: "iam",
                table: "SystemCredentials");

            migrationBuilder.DropIndex(
                name: "IX_SystemCredentials_TokenSourceKey",
                schema: "iam",
                table: "SystemCredentials");

            migrationBuilder.DropColumn(
                name: "TokenSourceKey",
                schema: "iam",
                table: "SystemCredentials");
        }
    }
}
