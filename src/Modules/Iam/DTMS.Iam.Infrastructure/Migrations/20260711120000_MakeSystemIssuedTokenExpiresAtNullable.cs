using System;
using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase S.8d — perpetual (never-expires) admin-issued system tokens.
    /// A perpetual token is minted with no <c>exp</c> claim, so its
    /// iam.SystemIssuedTokens row records <c>ExpiresAt = NULL</c>. That row
    /// doubles as the durable revocation allowlist the validator consults,
    /// so the column must accept NULL. This relaxes the NOT NULL constraint
    /// added in 20260629050000_AddSystemIssuedTokens.
    ///
    /// <para>Manual migration (dotnet-ef is incompatible with the .NET 10
    /// preview toolchain). MigrationId timestamp is later than every other
    /// module's so it sorts last in the shared public.__EFMigrationsHistory.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260711120000_MakeSystemIssuedTokenExpiresAtNullable")]
    public partial class MakeSystemIssuedTokenExpiresAtNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                schema: "iam",
                table: "SystemIssuedTokens",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting to NOT NULL requires a value for any perpetual-token
            // rows that accumulated a NULL. Backfill with the Unix epoch so
            // the constraint can be re-applied; such tokens would then read
            // as long-expired, which is the safe interpretation on rollback.
            migrationBuilder.Sql(
                "UPDATE iam.\"SystemIssuedTokens\" " +
                "SET \"ExpiresAt\" = TIMESTAMPTZ '1970-01-01 00:00:00+00' " +
                "WHERE \"ExpiresAt\" IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAt",
                schema: "iam",
                table: "SystemIssuedTokens",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
