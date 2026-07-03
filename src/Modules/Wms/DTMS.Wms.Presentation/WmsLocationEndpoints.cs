using DTMS.Iam.Application.Authorization;
using DTMS.Wms.Application.Commands.SyncWmsLocations;
using DTMS.Wms.Application.Queries.GetWmsLocations;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Wms.Presentation;

/// <summary>
/// HTTP surface for the WMS snapshot. All endpoints serve the local
/// <c>wms.Locations</c> table — never proxy directly to the external WMS.
/// The sync happens in the background so requests here are fast and
/// resilient to upstream outages.
///
/// The manual sync trigger is exposed for admin/debug use; ops normally
/// let the background poller drive the cadence.
/// </summary>
public static class WmsLocationEndpoints
{
    public static void MapWmsLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wms/locations")
            .WithTags("WMS Locations")
            .RequireAuthorization();

        // GET /api/v1/wms/locations?search=&page=&pageSize=&parentCode=&includeInactive=false
        // The order-form picker calls this on every keystroke (debounced
        // 300ms client-side); the response shape is intentionally shallow.
        group.MapGet("/", async (
            string? search,
            string? parentCode,
            int? page,
            int? pageSize,
            bool? includeInactive,
            ISender sender) =>
        {
            var result = await sender.Send(new GetWmsLocationsQuery(
                Search: search,
                ParentCode: parentCode,
                Page: page ?? 1,
                PageSize: pageSize ?? 20,
                IncludeInactive: includeInactive ?? false));

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        }).RequirePermission("dtms:order:read");

        // POST /api/v1/wms/locations/sync — admin-triggered pull. Idempotent
        // with the background poller (both go through the same handler +
        // global semaphore). Useful when ops needs to reflect an upstream
        // change immediately.
        group.MapPost("/sync", async (ISender sender) =>
        {
            var result = await sender.Send(new SyncWmsLocationsCommand());
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        }).RequirePermission("dtms:facility:warehouse:write");
    }
}
