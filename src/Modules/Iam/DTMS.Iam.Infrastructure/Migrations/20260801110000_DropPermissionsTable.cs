using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Drops <c>iam.Permissions</c>. The permission catalog has been
    /// code-served (<c>Permissions.All</c>) since the catalog endpoints went
    /// read-only — the table had no remaining readers, writers, or inbound
    /// FKs (SystemClientPermissions stores bare codes). Follows the
    /// <c>20260801100000_DropRolesAndRolePermissions</c> cutover to External
    /// Auth as the sole source of user permissions.
    ///
    /// <para>On a fresh database the historical seed migrations still create
    /// and populate the table; this migration simply drops it at the end of
    /// the chain. Down recreates the empty schema only.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260801110000_DropPermissionsTable")]
    public partial class DropPermissionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS iam.""Permissions"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS iam.""Permissions"" (
                    ""Code"" character varying(120) NOT NULL,
                    ""Description"" character varying(300) NOT NULL,
                    ""Module"" character varying(50) NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_Permissions"" PRIMARY KEY (""Code"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_Permissions_Module""
                    ON iam.""Permissions"" (""Module"");
            ");
        }
    }
}
