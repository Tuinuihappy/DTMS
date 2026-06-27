using DTMS.DeliveryOrder.Presentation;
using AMR.DeliveryPlanning.Dispatch.Presentation;
using DTMS.Facility.Presentation;
using DTMS.Fleet.Presentation;
using AMR.DeliveryPlanning.Planning.Presentation;
using DTMS.Transport.Amr.Webhooks;
using DTMS.Transport.Manual.Presentation;

namespace AMR.DeliveryPlanning.Api.Modules;

/// <summary>
/// Maps all module Minimal API endpoints onto the application pipeline.
/// </summary>
public static class ModuleEndpointRegistration
{
    public static WebApplication MapAllModuleEndpoints(this WebApplication app)
    {
        app.MapFacilityEndpoints();
        app.MapWarehouseEndpoints();   // Phase 2.7a — separate file from MapEndpoints to keep AMR vs Warehouse surfaces distinct
        app.MapFleetEndpoints();
        app.MapDeliveryOrderEndpoints();
        app.MapItemEndpoints();
        app.MapDashboardEndpoints();
        app.MapFleetDashboardEndpoints();
        app.MapReportsEndpoints();
        app.MapDispatchReportsEndpoints();
        app.MapPlanningReportsEndpoints();
        app.MapAdminProjectionsEndpoints();
        app.MapAdminWorkflowEndpoints();
        app.MapPlanningEndpoints();
        app.MapDispatchEndpoints();
        app.MapRiot3Webhooks();
        app.MapOperatorEndpoints();   // Phase 4.2 — /api/operator/* (Manual transport mode)
        app.MapAdminManualOperatorEndpoints();   // Phase 4.6 — /api/v1/admin/manual/*

        return app;
    }
}
