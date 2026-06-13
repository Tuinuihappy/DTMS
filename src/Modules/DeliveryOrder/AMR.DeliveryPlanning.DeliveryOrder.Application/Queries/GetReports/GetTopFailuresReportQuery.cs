using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetReports;

/// <summary>
/// Phase P5.3 — Top failure reasons report. Counts orders by
/// FailureReason text, descending. Source: <c>bi.OrderFacts</c> rows
/// whose FinalStatus is in {Failed, Cancelled, Rejected, Held}.
/// </summary>
public record GetTopFailuresReportQuery(
    DateTime FromUtc, DateTime ToUtc, int Limit = 20) : IQuery<TopFailuresReportResponse>;

public record FailureReasonRow(
    string Reason,
    string FinalStatus,
    int Count,
    double PctOfFailures);

public record TopFailuresReportResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalFailedOrders,
    IReadOnlyList<FailureReasonRow> Rows);

public class GetTopFailuresReportQueryHandler
    : IQueryHandler<GetTopFailuresReportQuery, TopFailuresReportResponse>
{
    private const int MaxWindowDays = 90;
    private static readonly HashSet<string> FailureStatuses = new(StringComparer.OrdinalIgnoreCase)
    { "Failed", "Cancelled", "Rejected", "Held" };

    private readonly IOrderFactsReadRepository _repo;

    public GetTopFailuresReportQueryHandler(IOrderFactsReadRepository repo) => _repo = repo;

    public async Task<Result<TopFailuresReportResponse>> Handle(
        GetTopFailuresReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<TopFailuresReportResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<TopFailuresReportResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var facts = await _repo.QueryAsync(
            new OrderFactsFilters(request.FromUtc, request.ToUtc, null, null, null), ct);

        var failed = facts.Where(f => FailureStatuses.Contains(f.FinalStatus)).ToList();
        var totalFailed = failed.Count;

        var rows = failed
            .GroupBy(f => new
            {
                // Collapse null / whitespace into a sentinel so they aggregate
                // together instead of fragmenting into separate "rows" the
                // analyst can't action on.
                Reason = string.IsNullOrWhiteSpace(f.FailureReason)
                    ? "(no reason captured)"
                    : f.FailureReason.Trim(),
                f.FinalStatus,
            })
            .Select(g => new FailureReasonRow(
                Reason: g.Key.Reason,
                FinalStatus: g.Key.FinalStatus,
                Count: g.Count(),
                PctOfFailures: totalFailed > 0 ? (double)g.Count() / totalFailed : 0))
            .OrderByDescending(r => r.Count)
            .Take(request.Limit)
            .ToList();

        return Result<TopFailuresReportResponse>.Success(new TopFailuresReportResponse(
            request.FromUtc, request.ToUtc, totalFailed, rows));
    }
}
