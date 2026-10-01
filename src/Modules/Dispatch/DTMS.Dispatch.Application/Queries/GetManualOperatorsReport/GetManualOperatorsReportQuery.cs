using DTMS.Dispatch.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetManualOperatorsReport;

/// <summary>
/// Manual operators report — the per-person breakdown of the "(manual)"
/// row on the vehicle performance report. Groups Manual trips (no
/// AmrTripExtension) by <c>Trips.AcknowledgedBy</c>, the OMS username of
/// whoever first accepted the trip.
///
/// <para>That is the closest DTMS has to "who carried it", but it is not
/// always the carrier, and some usernames are shared accounts. Trips with
/// no acknowledger collapse into "(not acknowledged)".</para>
///
/// <para>Timing uses the same <c>TimeToCompleteSec</c> as the vehicle
/// report so the two stay comparable.</para>
/// </summary>
public record GetManualOperatorsReportQuery(
    DateTime FromUtc, DateTime ToUtc) : IQuery<ManualOperatorsResponse>;

public record ManualOperatorRow(
    string Operator,
    int TotalTrips,
    int Completed,
    int Failed,
    int Rejected,
    int Cancelled,
    int InProgress,
    double SuccessRate,
    double? MedianTimeToCompleteSec,
    double? P95TimeToCompleteSec,
    int SlaBreached);

public record ManualOperatorsResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalTrips,
    IReadOnlyList<ManualOperatorRow> Rows);

public class GetManualOperatorsReportQueryHandler
    : IQueryHandler<GetManualOperatorsReportQuery, ManualOperatorsResponse>
{
    public const string NotAcknowledged = "(not acknowledged)";
    private const int MaxWindowDays = 90;

    private readonly ITripFactsReadRepository _repo;

    public GetManualOperatorsReportQueryHandler(ITripFactsReadRepository repo) => _repo = repo;

    public async Task<Result<ManualOperatorsResponse>> Handle(
        GetManualOperatorsReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<ManualOperatorsResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<ManualOperatorsResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var trips = (await _repo.QueryAsync(
            new TripFactsFilters(request.FromUtc, request.ToUtc, null, null), ct))
            .Where(t => t.IsManual)
            .ToList();

        var rows = trips
            .GroupBy(t => string.IsNullOrWhiteSpace(t.AcknowledgedBy) ? NotAcknowledged : t.AcknowledgedBy.Trim())
            .Select(g =>
            {
                var total = g.Count();
                var completed = g.Count(t => t.FinalStatus == "Completed");
                var failed = g.Count(t => t.FinalStatus == "Failed");
                var rejected = g.Count(t => t.FinalStatus == "Rejected");
                var cancelled = g.Count(t => t.FinalStatus == "Cancelled");
                var samples = g.Where(t => t.TimeToCompleteSec.HasValue)
                               .Select(t => (double)t.TimeToCompleteSec!.Value)
                               .OrderBy(x => x)
                               .ToArray();
                // Success rate over terminal trips only, same as the vehicle report.
                var terminal = completed + failed + rejected + cancelled;

                return new ManualOperatorRow(
                    Operator: g.Key,
                    TotalTrips: total,
                    Completed: completed,
                    Failed: failed,
                    Rejected: rejected,
                    Cancelled: cancelled,
                    InProgress: total - terminal,
                    SuccessRate: terminal > 0 ? (double)completed / terminal : 0,
                    MedianTimeToCompleteSec: Percentile(samples, 0.5),
                    P95TimeToCompleteSec: Percentile(samples, 0.95),
                    SlaBreached: g.Count(t => t.SlaCompleteBreached == true));
            })
            .OrderByDescending(r => r.TotalTrips)
            .ToList();

        return Result<ManualOperatorsResponse>.Success(new ManualOperatorsResponse(
            request.FromUtc, request.ToUtc, trips.Count, rows));
    }

    private static double? Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return null;
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
