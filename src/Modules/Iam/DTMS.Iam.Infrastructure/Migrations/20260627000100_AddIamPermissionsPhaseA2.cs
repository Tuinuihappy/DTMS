using DTMS.Iam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Phase A.2 — extend the permission catalog from Facility-only to cover
    // every protected endpoint in DeliveryOrder, Dispatch, Fleet, Planning,
    // and the Transport.Manual operator PWA. Admin keeps the dtms:* wildcard
    // (no new rows). The original seed assigned management + read-only
    // perms to Supervisor / Operator system roles, but those roles were
    // removed from the default seed — operators now provision their own
    // role hierarchy via /admin/roles, so this migration ships permissions
    // only and no longer seeds non-admin role mappings.
    [DbContext(typeof(IamDbContext))]
    [Migration("20260627000100_AddIamPermissionsPhaseA2")]
    public partial class AddIamPermissionsPhaseA2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO iam.""Permissions"" (""Code"", ""Description"", ""Module"", ""CreatedAt"") VALUES
                  -- DeliveryOrder
                  ('dtms:order:read',              'Read delivery orders, items, timeline, audit', 'DeliveryOrder', now()),
                  ('dtms:order:write',             'Create / update / delete draft orders',        'DeliveryOrder', now()),
                  ('dtms:order:submit',            'Submit a draft order',                          'DeliveryOrder', now()),
                  ('dtms:order:confirm',           'Manually confirm a validated order',            'DeliveryOrder', now()),
                  ('dtms:order:reject',            'Reject an order',                               'DeliveryOrder', now()),
                  ('dtms:order:cancel',            'Cancel order (single + bulk)',                  'DeliveryOrder', now()),
                  ('dtms:order:hold',              'Hold / release an order',                       'DeliveryOrder', now()),
                  ('dtms:order:reopen',            'Reopen a failed order (admin override)',        'DeliveryOrder', now()),
                  ('dtms:order:abandon',           'Abandon a stuck order (escape hatch)',          'DeliveryOrder', now()),
                  ('dtms:order:redispatch',        'Redispatch an order whose dispatch failed',     'DeliveryOrder', now()),
                  ('dtms:order:upstream',          'Pipeline create-from-upstream (SAP/OMS)',       'DeliveryOrder', now()),
                  ('dtms:order:bulk',              'Bulk submit orders',                            'DeliveryOrder', now()),
                  ('dtms:order:notify-oms',        'Resend upstream OMS shipment notification',     'DeliveryOrder', now()),
                  ('dtms:order:pod',               'Submit POD scan (per-item or batch)',           'DeliveryOrder', now()),
                  ('dtms:order:item:read',         'Search items across orders',                    'DeliveryOrder', now()),
                  ('dtms:dashboard:read',          'Read dashboard projections (cross-aggregate)',  'DeliveryOrder', now()),

                  -- Dispatch
                  ('dtms:trip:read',               'Read trips, details, status-history, items',    'Dispatch', now()),
                  ('dtms:trip:pause',              'Pause / resume a trip',                         'Dispatch', now()),
                  ('dtms:trip:acknowledge',        'Acknowledge robot pass at checkpoint',          'Dispatch', now()),
                  ('dtms:trip:cancel',             'Cancel a trip (single + bulk)',                 'Dispatch', now()),
                  ('dtms:trip:retry',              'Reissue a cancelled trip',                      'Dispatch', now()),
                  ('dtms:trip:exception',          'Raise / resolve trip exceptions',               'Dispatch', now()),
                  ('dtms:trip:pod',                'Capture trip-level proof of delivery',          'Dispatch', now()),

                  -- Fleet
                  ('dtms:vehicle:read',            'Read available vehicles + fleet KPI',           'Fleet', now()),
                  ('dtms:vehicle:write',           'Register vehicles, vehicle-types, state',       'Fleet', now()),
                  ('dtms:vehicle:import',          'Import vehicles from RIOT3',                    'Fleet', now()),
                  ('dtms:vehicle:maintenance',     'Create / complete maintenance records',         'Fleet', now()),
                  ('dtms:fleet:group:write',       'Manage vehicle groups',                         'Fleet', now()),
                  ('dtms:fleet:charging-policy:write', 'Upsert charging policies',                  'Fleet', now()),

                  -- Planning
                  ('dtms:planning:job:read',       'Read jobs, status-history, queue',              'Planning', now()),
                  ('dtms:planning:job:write',      'Create job from order',                         'Planning', now()),
                  ('dtms:planning:job:plan',       'Assign vehicle, commit, replan',                'Planning', now()),
                  ('dtms:planning:job:retry',      'Retry a failed envelope job',                   'Planning', now()),
                  ('dtms:planning:consolidate',    'Consolidate / cross-dock / milk-run / mpd',     'Planning', now()),
                  ('dtms:planning:cost-model:read','Read planner cost model',                       'Planning', now()),
                  ('dtms:planning:cost-model:write','Update planner cost model',                    'Planning', now()),
                  ('dtms:planning:action-template:read',  'Read ActionTemplate catalog',            'Planning', now()),
                  ('dtms:planning:action-template:write', 'Manage ActionTemplate catalog',          'Planning', now()),
                  ('dtms:planning:order-template:read',   'Read OrderTemplate catalog',             'Planning', now()),
                  ('dtms:planning:order-template:write',  'Manage OrderTemplate catalog',           'Planning', now()),
                  ('dtms:planning:order-template:create', 'Instantiate OrderTemplate to RIOT3',     'Planning', now()),

                  -- Operator PWA (Transport.Manual)
                  ('dtms:operator:profile:read',       'Read operator profile + assigned trips',    'TransportManual', now()),
                  ('dtms:operator:trip:acknowledge',   'Acknowledge an assigned trip',              'TransportManual', now()),
                  ('dtms:operator:trip:pickup',        'Record pickup at origin',                   'TransportManual', now()),
                  ('dtms:operator:trip:drop',          'Record drop at destination',                'TransportManual', now()),
                  ('dtms:operator:trip:complete',      'Complete a trip',                           'TransportManual', now()),
                  ('dtms:operator:geofence:override',  'Request geofence override (with reason)',   'TransportManual', now()),
                  ('dtms:operator:push:register',      'Register / unregister push subscriptions',  'TransportManual', now()),
                  ('dtms:operator:pod:upload',         'Request a presigned URL to upload POD',     'TransportManual', now()),

                  -- Cross-cutting reports (separate read vs export — CSV bulk download is more sensitive)
                  ('dtms:report:read',                 'Read aggregated reports (in-app)',          'Reports', now()),
                  ('dtms:report:export',               'Download report CSVs (bulk data)',          'Reports', now())
                ON CONFLICT (""Code"") DO NOTHING;
            ");

            // No role mappings seeded here. Admin already holds dtms:*
            // from Phase A. Supervisor + Operator system roles were
            // dropped from the default seed (see Phase B migration);
            // any custom role created via /admin/roles starts blank and
            // earns permissions through the same admin UI.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop role mappings before the permissions they reference.
            migrationBuilder.Sql(@"
                DELETE FROM iam.""RolePermissions""
                WHERE ""PermissionCode"" LIKE 'dtms:order:%'
                   OR ""PermissionCode"" LIKE 'dtms:trip:%'
                   OR ""PermissionCode"" LIKE 'dtms:vehicle:%'
                   OR ""PermissionCode"" LIKE 'dtms:fleet:%'
                   OR ""PermissionCode"" LIKE 'dtms:planning:%'
                   OR ""PermissionCode"" LIKE 'dtms:operator:%'
                   OR ""PermissionCode"" LIKE 'dtms:report:%'
                   OR ""PermissionCode"" = 'dtms:dashboard:read';
            ");
            migrationBuilder.Sql(@"
                DELETE FROM iam.""Permissions""
                WHERE ""Module"" IN ('DeliveryOrder','Dispatch','Fleet','Planning','TransportManual','Reports');
            ");
        }
    }
}
