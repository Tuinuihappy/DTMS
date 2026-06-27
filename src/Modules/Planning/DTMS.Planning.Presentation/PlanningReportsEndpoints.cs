using System.Globalization;
using System.Text;
using DTMS.Planning.Application.Projections;
using DTMS.Planning.Application.Queries.GetJobFailuresReport;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Planning.Presentation;

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

        // Raw JobFacts CSV — analyst follow-up for the job-failures
        // report. Mirrors /trips-export pattern in DispatchReportsEndpoints.
        // Replaces the prior workaround where the frontend reused
        // trips-export here (trips ≠ jobs — wrong schema for analysts).
        group.MapGet("/jobs-export", async (
            DateTime? fromUtc, DateTime? toUtc,
            string? finalStatus, string? failureCategory, int? minAttemptNumber,
            IJobFactsReadRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var rows = await repo.QueryAsync(new JobFactsFilters(
                from, to, finalStatus, minAttemptNumber, failureCategory), ct);

            http.Response.ContentType = "text/csv; charset=utf-8";
            http.Response.Headers.ContentDisposition =
                $"attachment; filename=\"jobs-{from:yyyyMMdd}-{to:yyyyMMdd}.csv\"";
            await WriteCsvAsync(http.Response.Body, rows, ct);
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

    private static async Task WriteCsvAsync(
        Stream stream, IReadOnlyList<JobFactsEntry> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(string.Join(",", new[]
        {
            "JobId", "DeliveryOrderId", "AssignedVehicleId", "LatestTripId",
            "VendorOrderKey", "FinalStatus", "FailureReason", "FailureCategory",
            "AttemptNumber",
            "CreatedAt", "AssignedAt", "CommittedAt", "DispatchedAt", "ExecutingAt",
            "CompletedAt", "FailedAt", "CancelledAt",
            "TimeToDispatchSec", "TimeToCompleteSec", "SlaDispatchBreached",
            "UpdatedAt",
        }));

        var inv = CultureInfo.InvariantCulture;
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", new[]
            {
                r.JobId.ToString(),
                r.DeliveryOrderId.ToString(),
                r.AssignedVehicleId?.ToString() ?? "",
                r.LatestTripId?.ToString() ?? "",
                Csv(r.VendorOrderKey),
                Csv(r.FinalStatus),
                Csv(r.FailureReason),
                Csv(r.FailureCategory),
                r.AttemptNumber.ToString(inv),
                Dt(r.CreatedAt), Dt(r.AssignedAt), Dt(r.CommittedAt),
                Dt(r.DispatchedAt), Dt(r.ExecutingAt),
                Dt(r.CompletedAt), Dt(r.FailedAt), Dt(r.CancelledAt),
                r.TimeToDispatchSec?.ToString(inv) ?? "",
                r.TimeToCompleteSec?.ToString(inv) ?? "",
                r.SlaDispatchBreached?.ToString() ?? "",
                Dt(r.UpdatedAt),
            }));
        }
        await writer.FlushAsync(ct);
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        return needsQuote ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static string Dt(DateTime? d)
        => d?.ToString("O", CultureInfo.InvariantCulture) ?? "";

    private static string Dt(DateTime d)
        => d.ToString("O", CultureInfo.InvariantCulture);
}
