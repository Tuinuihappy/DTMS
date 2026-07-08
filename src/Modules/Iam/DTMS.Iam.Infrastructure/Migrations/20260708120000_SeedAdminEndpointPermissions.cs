using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <summary>
    /// Seeds the six permission codes newly enforced on the <c>/api/v1/admin/*</c>
    /// endpoints so they are catalogued (visible in the admin permission list) and
    /// grantable. The codes are defined in
    /// <see cref="DTMS.Iam.Application.Authorization.Permissions"/> and referenced by
    /// the endpoints via <c>.RequirePermission(...)</c>; this migration keeps the
    /// <c>iam.Permissions</c> catalog in sync (the <c>EveryCatalogCode_IsSeeded</c>
    /// architecture test enforces catalog ⊆ seeded).
    ///
    /// <para>All six are `dtms:*`-prefixed, so the existing <c>('Admin','dtms:*')</c>
    /// role grant already satisfies them — no RolePermissions change. Mirrors
    /// <c>20260707130000_AddSourceTripPermissionsToCatalog</c>. Idempotent
    /// (<c>ON CONFLICT DO NOTHING</c>); Down removes only these six codes.</para>
    /// </summary>
    [DbContext(typeof(IamDbContext))]
    [Migration("20260708120000_SeedAdminEndpointPermissions")]
    public partial class SeedAdminEndpointPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  ('dtms:dispatch:trip:force',        'Force a trip through a lifecycle transition (admin override)', 'Dispatch',      now()),
                  ('dtms:deliveryorder:order:replan', 'Replan a stuck order (admin override)',                        'DeliveryOrder', now()),
                  ('dtms:operator:pool:read',         'Read the operator pool + manual board (admin)',                'TransportManual', now()),
                  ('dtms:operator:geofence:review',   'Review (approve/deny) geofence override requests',             'TransportManual', now()),
                  ('dtms:iam:projection:manage',      'Inspect / replay / rebuild projections',                       'Iam',           now()),
                  ('dtms:iam:outbox:manage',          'Inspect / replay / delete outbox DLQ messages',                'Iam',           now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions""
                 WHERE ""Code"" IN (
                   'dtms:dispatch:trip:force',
                   'dtms:deliveryorder:order:replan',
                   'dtms:operator:pool:read',
                   'dtms:operator:geofence:review',
                   'dtms:iam:projection:manage',
                   'dtms:iam:outbox:manage'
                 );
            ");
        }
    }
}
