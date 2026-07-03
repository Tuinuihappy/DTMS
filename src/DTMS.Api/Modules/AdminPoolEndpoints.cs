using DTMS.Transport.Manual.Application.Queries.GetPoolSummary;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Api.Modules;

/// <summary>
/// WMS PR-4b (PR-G) — Dispatcher admin surface for the operator pool.
///
/// One endpoint today (summary snapshot); future admin actions
/// (cancel pooled trip, force-reassign) are deferred per ADR-011 §"Consequences" —
/// force-assign against the pool violates the single-owner invariant and
/// is intentionally NOT exposed until an ADR revision authorizes it.
///
/// Auth mirrors AdminOutboxEndpoints: MapGroup under /api/v1/admin with
/// RequireAuthorization(). Any authenticated dispatcher-or-above can hit
/// this — read-only surface, no privileged data.
/// </summary>
public static class AdminPoolEndpoints
{
    public static void MapAdminPoolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/pool")
                       .WithTags("Admin")
                       .RequireAuthorization();

        // GET /api/v1/admin/pool/summary
        // Returns { poolDepth, oldestWaitedSeconds, activeOperators, claimedInFlight }
        group.MapGet("/summary", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPoolSummaryQuery(), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { Error = result.Error });
        });
    }
}
