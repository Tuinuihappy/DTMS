using AMR.DeliveryPlanning.Fleet.Application.Queries.GetFleetUtilization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Fleet.Presentation;

/// <summary>
/// Phase P3.2 — Fleet-side dashboard endpoints. Lives alongside the
/// DeliveryOrder-side DashboardEndpoints under the same
/// /api/v1/dashboard route group.
/// </summary>
public static class FleetDashboardEndpoints
{
    public static void MapFleetDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard").RequireAuthorization();

        // GET /api/v1/dashboard/fleet-utilization?fromUtc=&toUtc=
        // Hour-bucketed snapshots of vehicle state distribution from
        // fleet.FleetUtilizationHourly (written by the snapshot service
        // every minute, bucket-grained per hour).
        group.MapGet("/fleet-utilization", async (DateTime? fromUtc, DateTime? toUtc, ISender sender) =>
        {
            var now = DateTime.UtcNow;
            var to = toUtc ?? new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
            var from = fromUtc ?? to.AddHours(-24);

            var result = await sender.Send(new GetFleetUtilizationQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });
    }
}
