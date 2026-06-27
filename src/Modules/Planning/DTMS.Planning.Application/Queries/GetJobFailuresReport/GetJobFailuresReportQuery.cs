using DTMS.Planning.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetJobFailuresReport;

/// <summary>
/// Phase #9 — Job failures broken down by structured
/// <c>JobFailureCategory</c> (the b13 enum). Companion to the existing
/// "Top failures" report — that one answers "which orders failed and
/// why?" sourcing from OrderFacts.FailureReason text. This one answers
/// "what are the technical reasons jobs don't dispatch?" sourcing from
/// JobFacts.FailureCategory + FailureReason.
///
/// <para>Cells are flattened: one row per (category, reason) so the
/// table can show a rolled-up text breakdown under each category. The
/// chart aggregates further to one bar per category.</para>
/// </summary>
public record GetJobFailuresReportQuery(
    DateTime FromUtc, DateTime ToUtc) : IQuery<JobFailuresReportResponse>;

public record JobFailureCategoryTotal(
    string Category,
    int Count,
    double Pct);

public record JobFailureRow(
    string Category,
    string Reason,
    int Count,
    int RetriedCount,    // jobs that hit AttemptNumber > 1
    double PctOfTotal);

public record JobFailuresReportResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalFailures,
    IReadOnlyList<JobFailureCategoryTotal> CategoryTotals,
    IReadOnlyList<JobFailureRow> Rows);

public class GetJobFailuresReportQueryHandler
    : IQueryHandler<GetJobFailuresReportQuery, JobFailuresReportResponse>
{
    private const int MaxWindowDays = 90;
    // Jobs that ended in either state count as failures here. JobFacts
    // also has 'None' for non-terminal-failure rows so we filter those out.
    private static readonly HashSet<string> FailureStatuses = new(StringComparer.OrdinalIgnoreCase)
    { "Failed", "Cancelled" };

    private readonly IJobFactsReadRepository _repo;

    public GetJobFailuresReportQueryHandler(IJobFactsReadRepository repo) => _repo = repo;

    public async Task<Result<JobFailuresReportResponse>> Handle(
        GetJobFailuresReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<JobFailuresReportResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<JobFailuresReportResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var facts = await _repo.QueryAsync(
            new JobFactsFilters(request.FromUtc, request.ToUtc, null, null), ct);

        var failed = facts.Where(f => FailureStatuses.Contains(f.FinalStatus)).ToList();
        var total = failed.Count;

        var categoryTotals = failed
            .GroupBy(f => f.FailureCategory)
            .Select(g => new JobFailureCategoryTotal(
                Category: g.Key,
                Count: g.Count(),
                Pct: total > 0 ? (double)g.Count() / total : 0))
            .OrderByDescending(c => c.Count)
            .ToList();

        var rows = failed
            .GroupBy(f => new
            {
                f.FailureCategory,
                Reason = string.IsNullOrWhiteSpace(f.FailureReason)
                    ? "(no reason captured)"
                    : f.FailureReason.Trim(),
            })
            .Select(g => new JobFailureRow(
                Category: g.Key.FailureCategory,
                Reason: g.Key.Reason,
                Count: g.Count(),
                RetriedCount: g.Count(f => f.AttemptNumber > 1),
                PctOfTotal: total > 0 ? (double)g.Count() / total : 0))
            .OrderByDescending(r => r.Count)
            .ToList();

        return Result<JobFailuresReportResponse>.Success(new JobFailuresReportResponse(
            request.FromUtc, request.ToUtc, total, categoryTotals, rows));
    }
}
