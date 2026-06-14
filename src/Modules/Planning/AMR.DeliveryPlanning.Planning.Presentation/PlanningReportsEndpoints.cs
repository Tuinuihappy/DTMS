using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobFailuresReport;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Planning.Presentation;

/// <summary>
/// Phase #9 — Reports backed by Planning projections (bi.JobFacts).
/// Lives in a dedicated group so the DeliveryOrder module doesn't need
/// a project reference to Planning.Application just to expose the
/// Job failures report. Parallels DispatchReportsEndpoints (vehicle
/// performance from TripFacts).
/// </summary>
public static class PlanningReportsEndpoints
{
    public static void MapPlanningReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization();

        group.MapGet("/job-failures", async (
            DateTime? fromUtc, DateTime? toUtc, ISender sender) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var result = await sender.Send(new GetJobFailuresReportQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });
    }

    private static (DateTime from, DateTime to) ResolveWindow(
        DateTime? fromUtc, DateTime? toUtc, int defaultDays)
    {
        var now = DateTime.UtcNow;
        var to = toUtc ?? new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        var from = fromUtc ?? to.AddDays(-defaultDays);
        return (from, to);
    }
}
