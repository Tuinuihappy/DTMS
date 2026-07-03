using DTMS.DeliveryOrder.Presentation;
using DTMS.Dispatch.Presentation;
using DTMS.Facility.Presentation;
using DTMS.Fleet.Presentation;
using DTMS.Iam.Presentation;
using DTMS.Planning.Presentation;
using DTMS.Transport.Amr.Webhooks;
using DTMS.Transport.Manual.Presentation;
using DTMS.Api.SystemCapabilities;
using DTMS.Wms.Presentation;

namespace DTMS.Api.Modules;

/// <summary>
/// Maps all module Minimal API endpoints onto the application pipeline.
/// </summary>
public static class ModuleEndpointRegistration
{
    public static WebApplication MapAllModuleEndpoints(this WebApplication app)
    {
        app.MapFacilityEndpoints();
        app.MapWmsLocationEndpoints();   // WMS PR-1 — /api/v1/wms/locations (list + manual sync trigger)
        app.MapSystemCapabilitiesEndpoints();   // WMS PR-4 — /api/v1/system/capabilities (feature flags)
        app.MapFleetEndpoints();
        app.MapDeliveryOrderEndpoints();
        // Phase S.2.2 — federated source-system endpoint group at
        // /api/v1/source/* (separate from the admin-side
        // /api/v1/delivery-orders/upstream so the system auth +
        // request-log middleware applies cleanly). Phase S.8e P3 dropped
        // the URL {key} segment — identity comes from the JWT sub claim.
        app.MapSourceSystemDeliveryOrderEndpoints();
        app.MapItemEndpoints();
        app.MapDashboardEndpoints();
        app.MapFleetDashboardEndpoints();
        app.MapReportsEndpoints();
        app.MapDispatchReportsEndpoints();
        app.MapPlanningReportsEndpoints();
        app.MapAdminProjectionsEndpoints();
        app.MapAdminWorkflowEndpoints();
        app.MapAdminOutboxEndpoints();  // Phase O3 — DLQ list / replay / delete
        app.MapAdminPoolEndpoints();    // WMS PR-4b (PR-G) — /api/v1/admin/pool/summary
        app.MapPlanningEndpoints();
        app.MapDispatchEndpoints();
        app.MapRiot3Webhooks();
        app.MapOperatorEndpoints();   // Phase 4.2 — /api/operator/* (Manual transport mode)
        app.MapAdminManualOperatorEndpoints();   // Phase 4.6 — /api/v1/admin/manual/*
        app.MapIamEndpoints();   // Permission System Phase B — /api/v1/iam/*
        app.MapSystemSubscriptionEndpoints();   // Phase S.3.1b — /api/v1/iam/systems/{key}/subscriptions
        app.MapSystemAdminEndpoints();   // Phase S.4 — /api/v1/iam/systems/* CRUD

        return app;
    }
}
