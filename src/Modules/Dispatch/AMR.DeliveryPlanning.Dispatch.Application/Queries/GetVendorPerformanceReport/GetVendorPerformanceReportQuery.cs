using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetVendorPerformanceReport;

/// <summary>
/// Phase P5.3 — Vendor performance report. Groups <c>bi.TripFacts</c>
/// rows by <c>VendorUpperKey</c> and computes throughput + success rate
/// + average lead time. Source rows without a VendorUpperKey
/// (legacy/internal trips) collapse into a "(none)" bucket so ops can
/// see them without losing the rest of the data.
/// </summary>
public record GetVendorPerformanceReportQuery(
    DateTime FromUtc, DateTime ToUtc) : IQuery<VendorPerformanceResponse>;

public record VendorPerformanceRow(
    string VendorUpperKey,
    int TotalTrips,
    int Completed,
    int Failed,
    int Cancelled,
    double SuccessRate,
    double? AvgTimeToCompleteSec,
    double? P95TimeToCompleteSec,
    int SlaBreached);

public record VendorPerformanceResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalTrips,
    IReadOnlyList<VendorPerformanceRow> Rows);

public class GetVendorPerformanceReportQueryHandler
    : IQueryHandler<GetVendorPerformanceReportQuery, VendorPerformanceResponse>
{
    private const int MaxWindowDays = 90;

    private readonly ITripFactsReadRepository _repo;

    public GetVendorPerformanceReportQueryHandler(ITripFactsReadRepository repo) => _repo = repo;

    public async Task<Result<VendorPerformanceResponse>> Handle(
        GetVendorPerformanceReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<VendorPerformanceResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<VendorPerformanceResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var trips = await _repo.QueryAsync(
            new TripFactsFilters(request.FromUtc, request.ToUtc, null, null), ct);

        var rows = trips
            .GroupBy(t => string.IsNullOrEmpty(t.VendorUpperKey) ? "(none)" : t.VendorUpperKey)
            .Select(g =>
            {
                var total = g.Count();
                var completed = g.Count(t => t.FinalStatus == "Completed");
                var failed = g.Count(t => t.FinalStatus == "Failed");
                var cancelled = g.Count(t => t.FinalStatus == "Cancelled");
                var samples = g.Where(t => t.TimeToCompleteSec.HasValue)
                               .Select(t => (double)t.TimeToCompleteSec!.Value)
                               .OrderBy(x => x)
                               .ToArray();
                var avg = samples.Length > 0 ? (double?)samples.Average() : null;
                var p95 = Percentile(samples, 0.95);
                var slaBreached = g.Count(t => t.SlaCompleteBreached == true);
                // Success rate is over terminal trips only — pending/in-progress
                // trips would otherwise dilute the number for vendors with a
                // lot of active work.
                var terminal = completed + failed + cancelled;
                var successRate = terminal > 0 ? (double)completed / terminal : 0;

                return new VendorPerformanceRow(
                    VendorUpperKey: g.Key,
                    TotalTrips: total,
                    Completed: completed,
                    Failed: failed,
                    Cancelled: cancelled,
                    SuccessRate: successRate,
                    AvgTimeToCompleteSec: avg,
                    P95TimeToCompleteSec: p95,
                    SlaBreached: slaBreached);
            })
            .OrderByDescending(r => r.TotalTrips)
            .ToList();

        return Result<VendorPerformanceResponse>.Success(new VendorPerformanceResponse(
            request.FromUtc, request.ToUtc, trips.Count, rows));
    }

    private static double? Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return null;
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
