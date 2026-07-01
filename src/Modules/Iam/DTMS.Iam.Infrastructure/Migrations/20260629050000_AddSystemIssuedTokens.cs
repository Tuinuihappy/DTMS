using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase S.8c — durable audit + backing store for admin-issued long-
    /// lived system JWTs. Row inserted on issue, updated on revoke. The
    /// hot-path revocation check lives in Redis (
    /// <c>iam:revoked-jti:{jti}</c>) — this table is the historical
    /// record + UI list. FK cascade so hard-deleting a SystemClient
    /// (Phase S.6 rare cleanup path) drops the issued-token audit rows
    /// together with everything else keyed to that system.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260629050000_AddSystemIssuedTokens")]
    public partial class AddSystemIssuedTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemIssuedTokens",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    SystemKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Jti = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RevokedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemIssuedTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemIssuedTokens_SystemClients",
                        column: x => x.SystemKey,
                        principalSchema: "iam",
                        principalTable: "SystemClients",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique on jti — a duplicate insert (retry / race) surfaces
            // loud instead of producing 2 rows sharing a token id.
            migrationBuilder.CreateIndex(
                name: "UX_SystemIssuedTokens_Jti",
                schema: "iam",
                table: "SystemIssuedTokens",
                column: "Jti",
                unique: true);

            // Admin UI's per-system list orders newest-first — index
            // matches so the sort is index-only.
            migrationBuilder.CreateIndex(
                name: "IX_SystemIssuedTokens_SystemKey_IssuedAt",
                schema: "iam",
                table: "SystemIssuedTokens",
                columns: new[] { "SystemKey", "IssuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemIssuedTokens",
                schema: "iam");
        }
    }
}
