using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: dead-domain removal — sweeps SystemClientPermissions grants for
    /// the four permission codes whose Facility features were deleted
    /// (topology overlays, shelf release, facility resources, and the
    /// warehouse:read leftover from the Warehouse aggregate dropped 20260703).
    ///
    /// <para>The catalog itself needs no data change: it is code-served
    /// (<c>Permissions.All</c>) since 20260801110000_DropPermissionsTable, so
    /// removing the PermissionDefinitions from Permissions.cs already hides
    /// the codes everywhere. iam.SystemClientPermissions is the ONLY table
    /// still holding raw permission codes (bare strings, no FK) — no seed
    /// ever granted these four, so this expects to delete 0 rows and only
    /// covers grants added by hand through the admin UI. The Admin
    /// <c>dtms:*</c> wildcard row is unaffected. User permissions live on the
    /// external auth service and are out of scope.</para>
    ///
    /// REVERSIBLE: Down() is a no-op — a deleted hand-added grant cannot be
    ///   reconstructed, and re-granting a code with no endpoint would be
    ///   meaningless anyway.
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260814000002_RemoveDeadFacilityPermissionGrants")]
    public partial class RemoveDeadFacilityPermissionGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""SystemClientPermissions""
                WHERE ""PermissionCode"" IN (
                    'dtms:facility:warehouse:read',
                    'dtms:facility:topology-overlay:write',
                    'dtms:facility:shelf:release',
                    'dtms:facility:resource:write'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see class summary.
        }
    }
}
