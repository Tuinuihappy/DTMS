using System.Globalization;
using System.Text;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Application.Queries.GetOrdersReport;
using DTMS.DeliveryOrder.Application.Queries.GetReports;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.DeliveryOrder.Presentation;

/// <summary>
/// Phase P5 — Reporting/BI endpoints backed by the <c>bi.OrderFacts</c>
/// projection. Two shapes per report:
///   - GET /summary returns the in-app preview (table cells + totals).
///   - GET /export streams CSV with one row per order matching the
///     same window — for analyst follow-up in Excel.
/// </summary>
public static class ReportsEndpoints
{
    public static void MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization();

        // GET /api/v1/reports/orders-summary
        // Group-by (Priority, FinalStatus) cells over the window. Default
        // window is the last 7 days. Handler caps at 90 days.
        group.MapGet("/orders-summary", async (
            DateTime? fromUtc, DateTime? toUtc,
            string? priority, string? finalStatus, string? sourceSystem,
            ISender sender) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var result = await sender.Send(new GetOrdersReportQuery(
                from, to, priority, finalStatus, sourceSystem));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Reporting.ReportRead);

        // ── P5.3 — 3 OrderFacts-backed reports + lead-time histogram ───
        //
        // All three share the same window resolver + 90d cap as the
        // orders-summary endpoint above. Aggregation lives in the
        // handlers; this group only marshalls query params + DTOs.

        group.MapGet("/sla-breach", async (
            DateTime? fromUtc, DateTime? toUtc, ISender sender) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var result = await sender.Send(new GetSlaBreachReportQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Reporting.ReportRead);

        group.MapGet("/top-failures", async (
            DateTime? fromUtc, DateTime? toUtc, int? limit, ISender sender) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var result = await sender.Send(new GetTopFailuresReportQuery(from, to, limit ?? 20));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Reporting.ReportRead);

        group.MapGet("/lead-time", async (
            DateTime? fromUtc, DateTime? toUtc, ISender sender) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var result = await sender.Send(new GetLeadTimeReportQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.Reporting.ReportRead);

        // GET /api/v1/reports/orders-export
        // Streams CSV of every OrderFacts row in the window. The repo
        // already caps at 50k rows; the writer streams row-by-row so
        // memory stays flat even at the cap.
        group.MapGet("/orders-export", async (
            DateTime? fromUtc, DateTime? toUtc,
            string? priority, string? finalStatus, string? sourceSystem,
            IOrderFactsReadRepository repo, HttpContext http, CancellationToken ct) =>
        {
            var (from, to) = ResolveWindow(fromUtc, toUtc, defaultDays: 7);
            var rows = await repo.QueryAsync(new OrderFactsFilters(
                from, to, priority, finalStatus, sourceSystem), ct);

            http.Response.ContentType = "text/csv; charset=utf-8";
            http.Response.Headers.ContentDisposition =
                $"attachment; filename=\"orders-{from:yyyyMMdd}-{to:yyyyMMdd}.csv\"";
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
        Stream stream, IReadOnlyList<OrderFactsEntry> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(string.Join(",", new[]
        {
            "OrderId", "OrderRef", "SourceSystem", "Priority", "TransportMode",
            "RequestedBy", "FinalStatus", "FailureReason",
            "TotalItems", "TotalQuantity", "TotalWeightKg",
            "CreatedAt", "SubmittedAt", "ConfirmedAt", "DispatchedAt", "InProgressAt",
            "CompletedAt", "PartiallyCompletedAt", "FailedAt", "CancelledAt", "RejectedAt",
            "HeldAt", "ReleasedAt",
            "TimeToConfirmSec", "TimeToDispatchSec", "TimeToCompleteSec",
            "SlaConfirmBreached", "SlaCompleteBreached",
            "UpdatedAt",
        }));

        var inv = CultureInfo.InvariantCulture;
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", new[]
            {
                r.OrderId.ToString(),
                Csv(r.OrderRef),
                Csv(r.SourceSystem),
                Csv(r.Priority),
                Csv(r.TransportMode),
                Csv(r.RequestedBy),
                Csv(r.FinalStatus),
                Csv(r.FailureReason),
                r.TotalItems.ToString(inv),
                r.TotalQuantity.ToString(inv),
                r.TotalWeightKg.ToString(inv),
                Dt(r.CreatedAt), Dt(r.SubmittedAt), Dt(r.ConfirmedAt),
                Dt(r.DispatchedAt), Dt(r.InProgressAt),
                Dt(r.CompletedAt), Dt(r.PartiallyCompletedAt),
                Dt(r.FailedAt), Dt(r.CancelledAt), Dt(r.RejectedAt),
                Dt(r.HeldAt), Dt(r.ReleasedAt),
                r.TimeToConfirmSec?.ToString(inv) ?? "",
                r.TimeToDispatchSec?.ToString(inv) ?? "",
                r.TimeToCompleteSec?.ToString(inv) ?? "",
                r.SlaConfirmBreached?.ToString() ?? "",
                r.SlaCompleteBreached?.ToString() ?? "",
                Dt(r.UpdatedAt),
            }));
        }

        await writer.FlushAsync(ct);
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        // RFC 4180 minimal quoting: wrap if value contains a comma, quote,
        // CR, or LF — and double internal quotes.
        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        return needsQuote ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static string Dt(DateTime? d)
        => d?.ToString("O", CultureInfo.InvariantCulture) ?? "";
}
