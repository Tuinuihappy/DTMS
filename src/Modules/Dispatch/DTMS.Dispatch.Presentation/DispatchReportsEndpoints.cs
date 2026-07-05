using System.Globalization;
using System.Text;
using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Application.Queries.GetVehiclePerformanceReport;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Dispatch.Presentation;

/// <summary>
/// Phase P5.3 — Reports backed by Dispatch projections. Lives in a
/// dedicated group so the DeliveryOrder module doesn't need a project
/// reference to Dispatch.Application just to expose the vendor
/// performance report. CSV export of raw TripFacts rows is colocated
/// here for the same reason.
/// </summary>
public static class DispatchReportsEndpoints
{
    public static void MapDispatchReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization();

        // Phase #10 — renamed from /vendor-performance. The previous
        // endpoint grouped by per-order envelope key and produced an
        // unreadable chart. New grouping is the RIOT3 deviceKey
        // (physical robot identity).
        group.MapGet("/vehicle-performance", async (
            DateTime? fromUtc, DateTime? toUtc, ISender sender) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var result = await sender.Send(new GetVehiclePerformanceReportQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Reporting.ReportRead);

        // Raw TripFacts CSV — analyst follow-up for the vendor performance
        // report. Same shape + cap as the orders-export endpoint.
        group.MapGet("/trips-export", async (
            DateTime? fromUtc, DateTime? toUtc,
            string? vendorUpperKey, string? finalStatus,
            ITripFactsReadRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var rows = await repo.QueryAsync(new TripFactsFilters(
                from, to, vendorUpperKey, finalStatus), ct);

            http.Response.ContentType = "text/csv; charset=utf-8";
            http.Response.Headers.ContentDisposition =
                $"attachment; filename=\"trips-{from:yyyyMMdd}-{to:yyyyMMdd}.csv\"";
            await WriteCsvAsync(http.Response.Body, rows, ct);
        }).RequirePermission(Permissions.Reporting.ReportExport);
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
        Stream stream, IReadOnlyList<TripFactsEntry> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(string.Join(",", new[]
        {
            "TripId", "DeliveryOrderId", "JobId", "VehicleId",
            "VendorUpperKey", "FinalStatus", "FailureReason", "PauseCount",
            "CreatedAt", "StartedAt", "FirstPausedAt", "LastResumedAt",
            "CompletedAt", "FailedAt", "CancelledAt",
            "TimeToStartSec", "TimeToCompleteSec", "SlaCompleteBreached",
            "UpdatedAt",
        }));

        var inv = CultureInfo.InvariantCulture;
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", new[]
            {
                r.TripId.ToString(),
                r.DeliveryOrderId?.ToString() ?? "",
                r.JobId?.ToString() ?? "",
                r.VehicleId?.ToString() ?? "",
                Csv(r.VendorUpperKey),
                Csv(r.FinalStatus),
                Csv(r.FailureReason),
                r.PauseCount.ToString(inv),
                Dt(r.CreatedAt), Dt(r.StartedAt),
                Dt(r.FirstPausedAt), Dt(r.LastResumedAt),
                Dt(r.CompletedAt), Dt(r.FailedAt), Dt(r.CancelledAt),
                r.TimeToStartSec?.ToString(inv) ?? "",
                r.TimeToCompleteSec?.ToString(inv) ?? "",
                r.SlaCompleteBreached?.ToString() ?? "",
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
}
