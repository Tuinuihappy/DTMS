using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Drops <c>iam.RolePermissions</c> and <c>iam.Roles</c>. External Auth
    /// is now the sole source of truth for user permissions — the LDAP JWT
    /// carries a <c>permission</c> claim array, so the role → permission
    /// lookup these tables backed never runs anymore (removed from
    /// PermissionClaimsTransformer the same day, together with the
    /// <c>/api/v1/iam/roles*</c> admin surface).
    ///
    /// <para>System-client permissions (<c>iam.SystemClientPermissions</c>)
    /// and the audit log are untouched. Historical audit rows keep their
    /// <c>Role</c> column values; the column is a plain string with no FK.
    /// Down recreates the schema only — the mapping data (Admin → dtms:*,
    /// ME/Operator grants) is not restored.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260801100000_DropRolesAndRolePermissions")]
    public partial class DropRolesAndRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS iam.""RolePermissions"";
                DROP TABLE IF EXISTS iam.""Roles"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS iam.""Roles"" (
                    ""Name"" character varying(50) NOT NULL,
                    ""Description"" character varying(300) NOT NULL,
                    ""IsSystem"" boolean NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_Roles"" PRIMARY KEY (""Name"")
                );
                CREATE TABLE IF NOT EXISTS iam.""RolePermissions"" (
                    ""Role"" character varying(50) NOT NULL,
                    ""PermissionCode"" character varying(120) NOT NULL,
                    CONSTRAINT ""PK_RolePermissions"" PRIMARY KEY (""Role"", ""PermissionCode""),
                    CONSTRAINT ""FK_RolePermissions_Roles_Role"" FOREIGN KEY (""Role"")
                        REFERENCES iam.""Roles"" (""Name"") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_RolePermissions_Role""
                    ON iam.""RolePermissions"" (""Role"");
            ");
        }
    }
}
