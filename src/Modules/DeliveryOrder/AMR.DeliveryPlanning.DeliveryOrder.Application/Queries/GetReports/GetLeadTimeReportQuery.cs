using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetReports;

/// <summary>
/// Phase P5.3 — Lead-time distribution report. Bucketizes
/// TimeToCompleteSec for orders that reached a terminal good state, so
/// the analyst can see "most orders complete in 1–4h with a long tail
/// in 24h+". Bucket edges are fixed (0–15min, 15min–1h, 1h–4h, 4h–8h,
/// 8h–24h, 24h+) — they match the operations team's mental model.
/// </summary>
public record GetLeadTimeReportQuery(
    DateTime FromUtc, DateTime ToUtc) : IQuery<LeadTimeReportResponse>;

public record LeadTimeBucket(
    string Label,
    int LowerBoundSec,
    int? UpperBoundSec,  // null = open upper bound
    int Count,
    double Pct);

public record LeadTimeReportResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalCompleted,
    double? AvgSec,
    double? P50Sec,
    double? P95Sec,
    IReadOnlyList<LeadTimeBucket> Buckets);

public class GetLeadTimeReportQueryHandler
    : IQueryHandler<GetLeadTimeReportQuery, LeadTimeReportResponse>
{
    private const int MaxWindowDays = 90;

    // Fixed bucket edges in seconds. Tuned to AMR delivery cadence —
    // most happy-path orders finish in 1–4h; 24h+ is "stuck or escalated".
    private static readonly (string Label, int Lower, int? Upper)[] Edges =
    {
        ("<15m",    0,         15 * 60),
        ("15m–1h",  15 * 60,   60 * 60),
        ("1h–4h",   60 * 60,   4 * 3600),
        ("4h–8h",   4 * 3600,  8 * 3600),
        ("8h–24h",  8 * 3600,  24 * 3600),
        ("24h+",    24 * 3600, null),
    };

    private readonly IOrderFactsReadRepository _repo;

    public GetLeadTimeReportQueryHandler(IOrderFactsReadRepository repo) => _repo = repo;

    public async Task<Result<LeadTimeReportResponse>> Handle(
        GetLeadTimeReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<LeadTimeReportResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<LeadTimeReportResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var facts = await _repo.QueryAsync(
            new OrderFactsFilters(request.FromUtc, request.ToUtc, null, null, null), ct);

        var samples = facts
            .Where(f => f.TimeToCompleteSec.HasValue)
            .Select(f => f.TimeToCompleteSec!.Value)
            .ToArray();

        var total = samples.Length;
        var avg = total > 0 ? (double?)samples.Average() : null;
        var sorted = samples.OrderBy(x => x).ToArray();
        var p50 = Percentile(sorted, 0.50);
        var p95 = Percentile(sorted, 0.95);

        var buckets = Edges.Select(edge =>
        {
            var count = samples.Count(s => s >= edge.Lower && (edge.Upper == null || s < edge.Upper));
            return new LeadTimeBucket(
                Label: edge.Label,
                LowerBoundSec: edge.Lower,
                UpperBoundSec: edge.Upper,
                Count: count,
                Pct: total > 0 ? (double)count / total : 0);
        }).ToList();

        return Result<LeadTimeReportResponse>.Success(new LeadTimeReportResponse(
            request.FromUtc, request.ToUtc, total, avg, p50, p95, buckets));
    }

    private static double? Percentile(int[] sorted, double p)
    {
        if (sorted.Length == 0) return null;
        // Nearest-rank percentile — good enough for ops dashboards, no need
        // for the interpolated R-7 / R-8 variants.
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
