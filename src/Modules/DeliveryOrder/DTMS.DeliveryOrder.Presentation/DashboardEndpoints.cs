using DTMS.DeliveryOrder.Application.Queries.GetOrderFunnel;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.DeliveryOrder.Presentation;

/// <summary>
/// Phase P3 — Dashboard read-model endpoints. Cross-aggregate views that
/// don't belong on the OrderEndpoints group because they're shaped by
/// the dashboard's needs rather than the Order aggregate's contract.
/// </summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard").RequireAuthorization();

        // GET /api/v1/dashboard/order-funnel?fromUtc=&toUtc=
        // Hour-bucketed status counters from OrderFunnelProjector. Powers
        // the KpiRail totals + the DispatchFunnel chart. Window capped at
        // 90 days in the handler.
        group.MapGet("/order-funnel", async (DateTime? fromUtc, DateTime? toUtc, ISender sender) =>
        {
            // Default to last 24 hours, end-exclusive on the current hour.
            var now = DateTime.UtcNow;
            var to = toUtc ?? new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
            var from = fromUtc ?? to.AddHours(-24);

            var result = await sender.Send(new GetOrderFunnelQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:dashboard:read");
    }
}
