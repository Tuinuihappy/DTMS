using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using AMR.DeliveryPlanning.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.Api.Modules;

/// <summary>
/// Observability endpoint for the projection pipeline. Aggregates per-projector
/// inbox statistics across all four modules (DeliveryOrder / Planning /
/// Dispatch / Fleet) into a single shape the /admin/projections page can
/// render without N round-trips.
///
/// <para><b>Why an endpoint instead of pure Prometheus scraping:</b>
/// ProjectionMetrics emits OTel counters that suit Grafana dashboards but
/// can't be read back from inside the app. The ProjectionInbox tables are
/// the source of truth for "what did this projector process and when" —
/// queryable directly. Ops gets a single in-app glance; OTel keeps powering
/// alerts.</para>
///
/// <para><b>Lag semantic:</b> <c>lagSeconds</c> = NOW - last ProcessedAtUtc.
/// That's processing-time lag (how long since this projector last saved
/// anything), not event-time lag. For event-time lag the OTel histogram is
/// the right surface; this endpoint answers "is the projector alive?"</para>
/// </summary>
public static class AdminProjectionsEndpoints
{
    // Health thresholds (seconds since last processed event).
    private const int StaleAfterSec = 5 * 60;   // 5 min — projector might be wedged
    private const int IdleAfterSec  = 60 * 60;  // 1 hr — projector hasn't seen events; could be normal on a quiet system

    public static void MapAdminProjectionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization();

        group.MapGet("/projections", async (
            DeliveryOrderDbContext orderDb,
            PlanningDbContext planningDb,
            DispatchDbContext dispatchDb,
            FleetDbContext fleetDb,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var modules = new[]
            {
                await BuildModuleAsync(orderDb,    "DeliveryOrder", "deliveryorder", now, ct),
                await BuildModuleAsync(planningDb, "Planning",      "planning",      now, ct),
                await BuildModuleAsync(dispatchDb, "Dispatch",      "dispatch",      now, ct),
                await BuildModuleAsync(fleetDb,    "Fleet",         "fleet",         now, ct),
            };

            var totalProjectors = modules.Sum(m => m.Projectors.Count);
            var totalEvents = modules.Sum(m => m.Projectors.Sum(p => p.Processed));
            var totalHealthy = modules.Sum(m => m.Projectors.Count(p => p.Status == "healthy"));
            var totalStale = modules.Sum(m => m.Projectors.Count(p => p.Status == "stale"));
            var totalIdle = modules.Sum(m => m.Projectors.Count(p => p.Status == "idle"));

            return Results.Ok(new
            {
                generatedAtUtc = now,
                summary = new
                {
                    totalProjectors,
                    totalEventsProcessed = totalEvents,
                    healthy = totalHealthy,
                    stale = totalStale,
                    idle = totalIdle,
                },
                modules,
            });
        });
    }

    private static async Task<ModuleStatus> BuildModuleAsync<TContext>(
        TContext db, string moduleName, string schema, DateTime now, CancellationToken ct)
        where TContext : DbContext
    {
        // Each module owns its own ProjectionInbox table. We pull per-projector
        // counts + latest processed timestamp in a single GROUP BY.
        var inbox = db.Set<SharedKernel.Projection.InboxMessage>();
        var perProjector = await inbox
            .AsNoTracking()
            .GroupBy(m => m.ProjectorName)
            .Select(g => new
            {
                Name = g.Key,
                Processed = g.Count(),
                LastProcessedAtUtc = g.Max(m => m.ProcessedAtUtc),
            })
            .ToListAsync(ct);

        var projectors = perProjector
            .OrderBy(p => p.Name)
            .Select(p =>
            {
                var lagSec = (int)Math.Max(0, (now - p.LastProcessedAtUtc).TotalSeconds);
                var status =
                    lagSec > IdleAfterSec  ? "idle" :
                    lagSec > StaleAfterSec ? "stale" : "healthy";
                return new ProjectorStatus(
                    Name: p.Name,
                    Processed: p.Processed,
                    LastProcessedAtUtc: p.LastProcessedAtUtc,
                    LagSeconds: lagSec,
                    Status: status);
            })
            .ToList();

        return new ModuleStatus(
            Module: moduleName,
            Schema: schema,
            Projectors: projectors,
            InboxTotal: projectors.Sum(p => p.Processed));
    }

    private record ProjectorStatus(
        string Name,
        int Processed,
        DateTime LastProcessedAtUtc,
        int LagSeconds,
        string Status);

    private record ModuleStatus(
        string Module,
        string Schema,
        IReadOnlyList<ProjectorStatus> Projectors,
        int InboxTotal);
}
