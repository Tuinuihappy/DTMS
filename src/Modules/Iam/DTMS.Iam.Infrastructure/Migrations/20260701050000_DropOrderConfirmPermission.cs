using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Phase P5 — retire the <c>dtms:order:confirm</c> permission code
    /// after the standalone Confirm endpoint was folded into Submit.
    /// Every current holder (Operator role, oms SystemClient) gets its
    /// grant removed here so the code doesn't linger as dead data.
    ///
    /// <para><b>Order matters.</b> We delete grants before deleting the
    /// permission definition itself — a foreign-key from the grant tables
    /// would reject the definition delete otherwise. Both grant tables
    /// are pruned in the same transaction so no window exists where a
    /// definition-less grant could authorize a stale endpoint.</para>
    ///
    /// <para><b>Down.</b> Reinstates the row shape but not the grants —
    /// operators/systems that previously held the permission would need
    /// to be re-granted manually if this migration were ever rolled back.
    /// Acceptable because rolling back the code side would also restore
    /// the Confirm endpoint, at which point re-granting is the same admin
    /// action as any other permission change.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260701050000_DropOrderConfirmPermission")]
    public partial class DropOrderConfirmPermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""RolePermissions""
                WHERE ""PermissionCode"" = 'dtms:order:confirm';

                DELETE FROM iam.""SystemClientPermissions""
                WHERE ""PermissionCode"" = 'dtms:order:confirm';

                DELETE FROM iam.""Permissions""
                WHERE ""Code"" = 'dtms:order:confirm';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reinstate the permission definition only. Grants intentionally
            // NOT restored — see class remarks.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"")
                VALUES ('dtms:order:confirm', 'Confirm a validated delivery order')
                ON CONFLICT (""Code"") DO NOTHING;
            ");
        }
    }
}
