using DTMS.Dispatch.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Queries.GetVehiclePerformanceReport;

/// <summary>
/// Phase #10 — Vehicle performance report. Groups <c>bi.TripFacts</c>
/// rows by <c>VendorVehicleKey</c> (the deviceKey RIOT3 echoes on
/// TASK_PROCESSING — the physical robot identity) and computes
/// throughput, success rate, and lead-time stats per vehicle.
///
/// <para><b>Previous shape:</b> grouped by <c>VendorUpperKey</c>
/// (per-order envelope key), which produced one row per trip and
/// rendered an unreadable chart. The Vehicle dimension matches how
/// ops actually thinks about fleet performance: "which robot is
/// slow/fast/breaking down?"</para>
///
/// <para>Trips with no <c>VendorVehicleKey</c> collapse into one of two
/// buckets: "(manual)" for Manual trips (no robot, so never a key — this
/// is nearly all of them) and "(unassigned)" for AMR trips RIOT3 never
/// put a robot on (e.g. cancelled before start).</para>
///
/// <para><c>InProgress</c> counts every non-terminal trip, so
/// Completed + Failed + Rejected + Cancelled + InProgress = TotalTrips.</para>
/// </summary>
public record GetVehiclePerformanceReportQuery(
    DateTime FromUtc, DateTime ToUtc) : IQuery<VehiclePerformanceResponse>;

public record VehiclePerformanceRow(
    string VendorVehicleKey,
    string? VendorVehicleName,
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

public record VehiclePerformanceResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalTrips,
    IReadOnlyList<VehiclePerformanceRow> Rows);

public class GetVehiclePerformanceReportQueryHandler
    : IQueryHandler<GetVehiclePerformanceReportQuery, VehiclePerformanceResponse>
{
    private const int MaxWindowDays = 90;

    private readonly ITripFactsReadRepository _repo;

    public GetVehiclePerformanceReportQueryHandler(ITripFactsReadRepository repo) => _repo = repo;

    public async Task<Result<VehiclePerformanceResponse>> Handle(
        GetVehiclePerformanceReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<VehiclePerformanceResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<VehiclePerformanceResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var trips = await _repo.QueryAsync(
            new TripFactsFilters(request.FromUtc, request.ToUtc, null, null), ct);

        var rows = trips
            .GroupBy(t => !string.IsNullOrEmpty(t.VendorVehicleKey) ? t.VendorVehicleKey
                : t.IsManual ? "(manual)" : "(unassigned)")
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
                var median = Percentile(samples, 0.5);
                var p95 = Percentile(samples, 0.95);
                var slaBreached = g.Count(t => t.SlaCompleteBreached == true);
                // Success rate is over terminal trips only — pending/in-progress
                // trips would otherwise dilute the number for a vehicle that
                // has lots of active work.
                var terminal = completed + failed + rejected + cancelled;
                var successRate = terminal > 0 ? (double)completed / terminal : 0;

                // Newest non-empty name wins, so a robot RIOT3 renames shows
                // its current name. Null for the (manual)/(unassigned) buckets.
                var name = g.Where(t => !string.IsNullOrWhiteSpace(t.VendorVehicleName))
                            .OrderByDescending(t => t.CreatedAt)
                            .Select(t => t.VendorVehicleName)
                            .FirstOrDefault();

                return new VehiclePerformanceRow(
                    VendorVehicleKey: g.Key,
                    VendorVehicleName: name,
                    TotalTrips: total,
                    Completed: completed,
                    Failed: failed,
                    Rejected: rejected,
                    Cancelled: cancelled,
                    InProgress: total - terminal,
                    SuccessRate: successRate,
                    MedianTimeToCompleteSec: median,
                    P95TimeToCompleteSec: p95,
                    SlaBreached: slaBreached);
            })
            .OrderByDescending(r => r.TotalTrips)
            .ToList();

        return Result<VehiclePerformanceResponse>.Success(new VehiclePerformanceResponse(
            request.FromUtc, request.ToUtc, trips.Count, rows));
    }

    private static double? Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return null;
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
