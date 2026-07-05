using System.Reflection;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// One permission definition: the enforced <see cref="Code"/> plus the
/// <see cref="Description"/> and <see cref="Module"/> that the seed migration
/// carries. This record is the single source of truth for all three fields —
/// endpoints reference the definitions in <see cref="Permissions"/>, and a
/// guard test reconciles them against the seeded <c>iam.Permissions</c> rows.
///
/// Implicitly converts to its <see cref="Code"/> string so it drops straight
/// into <c>.RequirePermission(Permissions.X.Y)</c> with no call-site changes.
/// </summary>
public sealed record PermissionDefinition(string Code, string Description, string Module)
{
    public static implicit operator string(PermissionDefinition p) => p.Code;
    public override string ToString() => Code;
}

/// <summary>
/// Canonical permission catalog — see ADR-017. Grammar:
/// <c>dtms:&lt;module&gt;:&lt;resource&gt;:&lt;verb&gt;</c> (4 segments, lowercase,
/// kebab-case). Every protected endpoint must reference a member here instead
/// of a string literal; <c>PermissionCatalogTests</c> enforces that and checks
/// the grammar + catalog↔seed consistency.
///
/// The source-system scheme (<c>dtms:source:{key}:order:*</c>) lives in
/// <see cref="StandardSystemPermissions"/> and is intentionally NOT here — it is
/// a runtime-resolved external-vendor contract, exempt from this grammar.
/// </summary>
public static class Permissions
{
    public static class Dispatch
    {
        public static readonly PermissionDefinition TripRead = new("dtms:dispatch:trip:read", "Read trips, timeline, exceptions", "Dispatch");
        public static readonly PermissionDefinition TripPause = new("dtms:dispatch:trip:pause", "Pause / resume a trip", "Dispatch");
        public static readonly PermissionDefinition TripAcknowledge = new("dtms:dispatch:trip:acknowledge", "Acknowledge a trip", "Dispatch");
        public static readonly PermissionDefinition TripCancel = new("dtms:dispatch:trip:cancel", "Cancel a trip", "Dispatch");
        public static readonly PermissionDefinition TripRetry = new("dtms:dispatch:trip:retry", "Retry a failed trip", "Dispatch");
        public static readonly PermissionDefinition TripException = new("dtms:dispatch:exception:raise", "Raise a trip exception", "Dispatch");
        public static readonly PermissionDefinition TripPod = new("dtms:dispatch:pod:upload", "Upload trip proof of delivery", "Dispatch");
    }

    public static class DeliveryOrder
    {
        public static readonly PermissionDefinition OrderRead = new("dtms:deliveryorder:order:read", "Read delivery orders, items, timeline, audit", "DeliveryOrder");
        public static readonly PermissionDefinition OrderWrite = new("dtms:deliveryorder:order:write", "Create / update / delete draft orders", "DeliveryOrder");
        public static readonly PermissionDefinition OrderSubmit = new("dtms:deliveryorder:order:submit", "Submit a draft order", "DeliveryOrder");
        public static readonly PermissionDefinition OrderReject = new("dtms:deliveryorder:order:reject", "Reject an order", "DeliveryOrder");
        public static readonly PermissionDefinition OrderCancel = new("dtms:deliveryorder:order:cancel", "Cancel order (single + bulk)", "DeliveryOrder");
        public static readonly PermissionDefinition OrderHold = new("dtms:deliveryorder:order:hold", "Hold / release an order", "DeliveryOrder");
        public static readonly PermissionDefinition OrderReopen = new("dtms:deliveryorder:order:reopen", "Reopen a failed order (admin override)", "DeliveryOrder");
        public static readonly PermissionDefinition OrderAbandon = new("dtms:deliveryorder:order:abandon", "Abandon a stuck order (escape hatch)", "DeliveryOrder");
        public static readonly PermissionDefinition OrderRedispatch = new("dtms:deliveryorder:order:redispatch", "Redispatch an order whose dispatch failed", "DeliveryOrder");
        public static readonly PermissionDefinition OrderUpstream = new("dtms:deliveryorder:order:create-upstream", "Pipeline create-from-upstream (SAP/OMS)", "DeliveryOrder");
        public static readonly PermissionDefinition OrderBulk = new("dtms:deliveryorder:order:bulk-submit", "Bulk submit orders", "DeliveryOrder");
        public static readonly PermissionDefinition OrderNotifyOms = new("dtms:deliveryorder:order:notify-oms", "Resend an OMS notification", "DeliveryOrder");
        public static readonly PermissionDefinition OrderPod = new("dtms:deliveryorder:pod:upload", "Upload / manage proof of delivery", "DeliveryOrder");
        public static readonly PermissionDefinition ItemRead = new("dtms:deliveryorder:item:read", "Read order items", "DeliveryOrder");
    }

    public static class Fleet
    {
        public static readonly PermissionDefinition VehicleRead = new("dtms:fleet:vehicle:read", "Read vehicles", "Fleet");
        public static readonly PermissionDefinition VehicleWrite = new("dtms:fleet:vehicle:write", "Create / update / delete vehicles", "Fleet");
        public static readonly PermissionDefinition VehicleImport = new("dtms:fleet:vehicle:import", "Bulk-import vehicles", "Fleet");
        public static readonly PermissionDefinition VehicleMaintenance = new("dtms:fleet:vehicle:maintain", "Put a vehicle into / out of maintenance", "Fleet");
        public static readonly PermissionDefinition GroupWrite = new("dtms:fleet:group:write", "Manage fleet groups", "Fleet");
        public static readonly PermissionDefinition ChargingPolicyWrite = new("dtms:fleet:charging-policy:write", "Manage charging policy", "Fleet");
    }

    public static class Planning
    {
        public static readonly PermissionDefinition JobRead = new("dtms:planning:job:read", "Read planning jobs", "Planning");
        public static readonly PermissionDefinition JobWrite = new("dtms:planning:job:write", "Create / update / delete planning jobs", "Planning");
        public static readonly PermissionDefinition JobPlan = new("dtms:planning:job:plan", "Run planning on a job", "Planning");
        public static readonly PermissionDefinition JobRetry = new("dtms:planning:job:retry", "Retry a planning job", "Planning");
        public static readonly PermissionDefinition Consolidate = new("dtms:planning:consolidation:run", "Consolidate orders into jobs", "Planning");
        public static readonly PermissionDefinition CostModelRead = new("dtms:planning:cost-model:read", "Read cost models", "Planning");
        public static readonly PermissionDefinition CostModelWrite = new("dtms:planning:cost-model:write", "Manage cost models", "Planning");
        public static readonly PermissionDefinition ActionTemplateRead = new("dtms:planning:action-template:read", "Read action templates", "Planning");
        public static readonly PermissionDefinition ActionTemplateWrite = new("dtms:planning:action-template:write", "Manage action templates", "Planning");
        public static readonly PermissionDefinition OrderTemplateRead = new("dtms:planning:order-template:read", "Read order templates", "Planning");
        public static readonly PermissionDefinition OrderTemplateWrite = new("dtms:planning:order-template:write", "Manage OrderTemplate catalog", "Planning");
        public static readonly PermissionDefinition OrderTemplateCreate = new("dtms:planning:order-template:instantiate", "Instantiate OrderTemplate to RIOT3", "Planning");
    }

    public static class Facility
    {
        public static readonly PermissionDefinition MapRead = new("dtms:facility:map:read", "Read maps", "Facility");
        public static readonly PermissionDefinition MapWrite = new("dtms:facility:map:write", "Manage maps", "Facility");
        public static readonly PermissionDefinition MapImport = new("dtms:facility:map:import", "Import maps", "Facility");
        public static readonly PermissionDefinition MapSync = new("dtms:facility:map:sync", "Sync maps from the vendor", "Facility");
        public static readonly PermissionDefinition StationRead = new("dtms:facility:station:read", "Read stations", "Facility");
        public static readonly PermissionDefinition StationWrite = new("dtms:facility:station:write", "Manage stations", "Facility");
        public static readonly PermissionDefinition StationForceOffline = new("dtms:facility:station:force-offline", "Force a station offline", "Facility");
        public static readonly PermissionDefinition WarehouseRead = new("dtms:facility:warehouse:read", "Read warehouses", "Facility");
        public static readonly PermissionDefinition WarehouseWrite = new("dtms:facility:warehouse:write", "Manage warehouses", "Facility");
        public static readonly PermissionDefinition TopologyOverlayWrite = new("dtms:facility:topology-overlay:write", "Manage topology overlays", "Facility");
        public static readonly PermissionDefinition ShelfRelease = new("dtms:facility:shelf:release", "Release a shelf", "Facility");
        public static readonly PermissionDefinition ProfileRead = new("dtms:facility:profile:read", "Read facility profiles", "Facility");
        public static readonly PermissionDefinition ProfileWrite = new("dtms:facility:profile:write", "Manage facility profiles", "Facility");
        public static readonly PermissionDefinition ResourceWrite = new("dtms:facility:resource:write", "Manage facility resources", "Facility");
    }

    public static class Iam
    {
        public static readonly PermissionDefinition PermissionRead = new("dtms:iam:permission:read", "Read the permission catalog", "Iam");
        public static readonly PermissionDefinition PermissionWrite = new("dtms:iam:permission:write", "Manage permissions", "Iam");
        public static readonly PermissionDefinition RoleRead = new("dtms:iam:role:read", "Read roles", "Iam");
        public static readonly PermissionDefinition RoleWrite = new("dtms:iam:role:write", "Manage roles and grants", "Iam");
        public static readonly PermissionDefinition AuditRead = new("dtms:iam:audit:read", "Read the permission audit log", "Iam");
        public static readonly PermissionDefinition SystemRead = new("dtms:iam:system:read", "Read system clients", "Iam");
        public static readonly PermissionDefinition SystemWrite = new("dtms:iam:system:write", "Manage system clients", "Iam");
        public static readonly PermissionDefinition SubscriptionRead = new("dtms:iam:subscription:read", "Read event subscriptions", "Iam");
        public static readonly PermissionDefinition SubscriptionWrite = new("dtms:iam:subscription:write", "Manage event subscriptions", "Iam");
    }

    public static class Operator
    {
        public static readonly PermissionDefinition ProfileRead = new("dtms:operator:profile:read", "Read own operator profile", "TransportManual");
        public static readonly PermissionDefinition TripAcknowledge = new("dtms:operator:trip:acknowledge", "Acknowledge an assigned trip", "TransportManual");
        public static readonly PermissionDefinition TripPickup = new("dtms:operator:trip:pickup", "Mark pickup", "TransportManual");
        public static readonly PermissionDefinition TripDrop = new("dtms:operator:trip:drop", "Mark drop", "TransportManual");
        public static readonly PermissionDefinition TripComplete = new("dtms:operator:trip:complete", "Complete a trip", "TransportManual");
        public static readonly PermissionDefinition GeofenceOverride = new("dtms:operator:geofence:override", "Request a geofence override", "TransportManual");
        public static readonly PermissionDefinition PushRegister = new("dtms:operator:push:register", "Register a push token", "TransportManual");
        public static readonly PermissionDefinition PodUpload = new("dtms:operator:pod:upload", "Upload proof of delivery", "TransportManual");
    }

    public static class Reporting
    {
        public static readonly PermissionDefinition ReportRead = new("dtms:reporting:report:read", "Read reports", "Reporting");
        public static readonly PermissionDefinition ReportExport = new("dtms:reporting:report:export", "Export reports", "Reporting");
        public static readonly PermissionDefinition DashboardRead = new("dtms:reporting:dashboard:read", "Read dashboards", "Reporting");
    }

    /// <summary>
    /// Every definition in the catalog, discovered by reflecting over the
    /// nested module classes. Used by the seed-reconciliation guard test and
    /// any admin surface that lists the catalog.
    /// </summary>
    public static readonly IReadOnlyList<PermissionDefinition> All =
        typeof(Permissions).GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(PermissionDefinition))
            .Select(f => (PermissionDefinition)f.GetValue(null)!)
            .ToList();
}
