using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetReports;

/// <summary>
/// Phase P5.3 — SLA breach rate report. Counts orders whose
/// TimeToConfirm > 4h and/or TimeToComplete > 24h, grouped by
/// Priority. Source: <c>bi.OrderFacts</c> generated columns.
/// </summary>
public record GetSlaBreachReportQuery(
    DateTime FromUtc, DateTime ToUtc) : IQuery<SlaBreachReportResponse>;

public record SlaBreachRow(
    string Priority,
    int TotalOrders,
    int ConfirmBreached,
    int CompleteBreached,
    double ConfirmBreachRate,   // 0..1
    double CompleteBreachRate);

public record SlaBreachReportResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalOrders,
    int TotalConfirmBreached,
    int TotalCompleteBreached,
    IReadOnlyList<SlaBreachRow> Rows);

public class GetSlaBreachReportQueryHandler
    : IQueryHandler<GetSlaBreachReportQuery, SlaBreachReportResponse>
{
    private const int MaxWindowDays = 90;
    private readonly IOrderFactsReadRepository _repo;

    public GetSlaBreachReportQueryHandler(IOrderFactsReadRepository repo) => _repo = repo;

    public async Task<Result<SlaBreachReportResponse>> Handle(
        GetSlaBreachReportQuery request, CancellationToken ct)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<SlaBreachReportResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<SlaBreachReportResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var facts = await _repo.QueryAsync(
            new OrderFactsFilters(request.FromUtc, request.ToUtc, null, null, null), ct);

        var rows = facts
            .GroupBy(f => f.Priority)
            .Select(g =>
            {
                var total = g.Count();
                var confirmBreached = g.Count(f => f.SlaConfirmBreached == true);
                var completeBreached = g.Count(f => f.SlaCompleteBreached == true);
                return new SlaBreachRow(
                    Priority: g.Key,
                    TotalOrders: total,
                    ConfirmBreached: confirmBreached,
                    CompleteBreached: completeBreached,
                    ConfirmBreachRate: total > 0 ? (double)confirmBreached / total : 0,
                    CompleteBreachRate: total > 0 ? (double)completeBreached / total : 0);
            })
            // Order by priority severity: Critical > High > Normal > Low.
            .OrderBy(r => r.Priority switch
            {
                "Critical" => 0, "High" => 1, "Normal" => 2, "Low" => 3, _ => 4
            })
            .ToList();

        return Result<SlaBreachReportResponse>.Success(new SlaBreachReportResponse(
            request.FromUtc, request.ToUtc,
            TotalOrders: facts.Count,
            TotalConfirmBreached: facts.Count(f => f.SlaConfirmBreached == true),
            TotalCompleteBreached: facts.Count(f => f.SlaCompleteBreached == true),
            Rows: rows));
    }
}
