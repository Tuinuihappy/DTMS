using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Phase B (Admin UI groundwork) — adds two new tables:
    // - iam.Roles            : the catalog of role names DTMS recognises.
    //                          System roles seeded from the existing
    //                          RolePermissions rows; admin-created roles
    //                          can be added/removed via the UI but only
    //                          take effect when External Auth issues
    //                          tokens carrying that role name.
    // - iam.PermissionAuditLog : append-only history of admin actions
    //                          (grant/revoke/CRUD). Backed by the
    //                          /admin/audit-log page in Phase B.2.
    //
    // Also introduces the IAM management permissions (dtms:iam:*) needed
    // to guard the new /api/v1/iam/* endpoints, and adds an FK so a role
    // delete cascades its permission mappings.
    [DbContext(typeof(IamDbContext))]
    [Migration("20260627000200_AddIamRolesAndAuditPhaseB")]
    public partial class AddIamRolesAndAuditPhaseB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Roles ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "iam",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Name);
                });

            // Seed the Admin system role (must exist before the FK on
            // RolePermissions is added or the FK creation fails). Other
            // roles are created on demand via the /admin/roles UI; the
            // platform intentionally ships with only the bootstrap admin
            // so each deployment defines its own staffing hierarchy.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Roles"" (""Name"", ""Description"", ""IsSystem"", ""CreatedAt"") VALUES
                  ('Admin', 'Full access (holds the dtms:* wildcard).', true, now())
                ON CONFLICT (""Name"") DO NOTHING;
            ");

            // Add FK so deleting a role wipes its mappings — Phase B admin
            // UI exposes role delete, so orphan rows would otherwise
            // accumulate and surface as "ghost" permissions.
            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_Roles_Role",
                schema: "iam",
                table: "RolePermissions",
                column: "Role",
                principalSchema: "iam",
                principalTable: "Roles",
                principalColumn: "Name",
                onDelete: ReferentialAction.Cascade);

            // ── Audit log ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "PermissionAuditLog",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorEmployeeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PermissionCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionAuditLog", x => x.Id);
                });

            // Audit listing is "newest first" so the index runs DESC on
            // OccurredAt. Secondary indexes back the UI filters (by actor
            // and by role).
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_PermissionAuditLog_OccurredAt"" ON iam.""PermissionAuditLog"" (""OccurredAt"" DESC);
                CREATE INDEX ""IX_PermissionAuditLog_ActorEmployeeId"" ON iam.""PermissionAuditLog"" (""ActorEmployeeId"");
                CREATE INDEX ""IX_PermissionAuditLog_Role"" ON iam.""PermissionAuditLog"" (""Role"") WHERE ""Role"" IS NOT NULL;
            ");

            // ── IAM management permissions ──────────────────────────────
            // No new Admin mappings needed — Admin already holds dtms:*
            // wildcard from Phase A, which matches every dtms:iam:* code.
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  ('dtms:iam:permission:read',  'List the permission catalog',         'Iam', now()),
                  ('dtms:iam:permission:write', 'Create / update / delete permissions','Iam', now()),
                  ('dtms:iam:role:read',        'List roles + their permissions',      'Iam', now()),
                  ('dtms:iam:role:write',       'Create / delete roles + grant perms', 'Iam', now()),
                  ('dtms:iam:audit:read',       'Read the IAM audit log',              'Iam', now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions"" WHERE ""Module"" = 'Iam';
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_RolePermissions_Roles_Role",
                schema: "iam",
                table: "RolePermissions");

            migrationBuilder.DropTable(name: "PermissionAuditLog", schema: "iam");
            migrationBuilder.DropTable(name: "Roles", schema: "iam");
        }
    }
}
