namespace DTMS.Api.VendorHealth;

public static class VendorHealthEndpoints
{
    public static IEndpointRouteBuilder MapVendorHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/vendors/health", (IVendorHealthStore store) =>
        {
            var snapshots = store.GetAll()
                .Select(VendorHealthDto.From)
                .OrderBy(s => s.Vendor)
                .ToArray();
            return Results.Ok(new { vendors = snapshots });
        })
        .AllowAnonymous()
        .WithTags("Health")
        .WithSummary("Vendor health snapshot")
        .WithDescription(
            "Returns the latest cached vendor health snapshots (one per vendor). "
            + "Updated in the background by Riot3HealthPollerService — calling this "
            + "endpoint does not trigger a probe. For realtime updates, subscribe "
            + "to DashboardHub with boardKey=\"vendor-health\".");

        return routes;
    }
}
