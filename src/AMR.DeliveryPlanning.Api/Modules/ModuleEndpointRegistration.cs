using AMR.DeliveryPlanning.DeliveryOrder.Presentation;
using AMR.DeliveryPlanning.Dispatch.Presentation;
using AMR.DeliveryPlanning.Facility.Presentation;
using AMR.DeliveryPlanning.Fleet.Presentation;
using AMR.DeliveryPlanning.Planning.Presentation;
using AMR.DeliveryPlanning.VendorAdapter.Feeder.Webhooks;

namespace AMR.DeliveryPlanning.Api.Modules;

/// <summary>
/// Maps all module Minimal API endpoints onto the application pipeline.
/// </summary>
public static class ModuleEndpointRegistration
{
    public static WebApplication MapAllModuleEndpoints(this WebApplication app)
    {
        app.MapFacilityEndpoints();
        app.MapFleetEndpoints();
        app.MapDeliveryOrderEndpoints();
        app.MapPlanningEndpoints();
        app.MapDispatchEndpoints();
        app.MapRiot3Webhooks();

        return app;
    }
}
