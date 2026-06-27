using DTMS.DeliveryOrder.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetOrdersReport;

public class GetOrdersReportQueryHandler
    : IQueryHandler<GetOrdersReportQuery, OrdersReportResponse>
{
    // Same 90-day cap as the funnel handler — analyst window queries
    // beyond a quarter belong in a real BI tool, not the in-app report.
    private const int MaxWindowDays = 90;

    private readonly IOrderFactsReadRepository _repo;

    public GetOrdersReportQueryHandler(IOrderFactsReadRepository repo) => _repo = repo;

    public async Task<Result<OrdersReportResponse>> Handle(
        GetOrdersReportQuery request, CancellationToken cancellationToken)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<OrdersReportResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<OrdersReportResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var filters = new OrderFactsFilters(
            FromCreatedAtUtc: request.FromUtc,
            ToCreatedAtUtc: request.ToUtc,
            Priority: request.Priority,
            FinalStatus: request.FinalStatus,
            SourceSystem: request.SourceSystem);

        // Pull facts, group in-process. 50k cap is fine — Postgres returns
        // the slice fast (indexed CreatedAt scan) and group-by-Priority
        // over 50k rows is microseconds in-memory.
        var facts = await _repo.QueryAsync(filters, cancellationToken);

        var cells = facts
            .GroupBy(f => new { f.Priority, f.FinalStatus })
            .Select(g => new OrdersReportCell(
                Priority: g.Key.Priority,
                FinalStatus: g.Key.FinalStatus,
                Count: g.Count(),
                SlaConfirmBreached: g.Count(f => f.SlaConfirmBreached == true),
                SlaCompleteBreached: g.Count(f => f.SlaCompleteBreached == true),
                AvgTimeToConfirmSec: g.Any(f => f.TimeToConfirmSec.HasValue)
                    ? g.Where(f => f.TimeToConfirmSec.HasValue).Average(f => (double)f.TimeToConfirmSec!.Value)
                    : null,
                AvgTimeToCompleteSec: g.Any(f => f.TimeToCompleteSec.HasValue)
                    ? g.Where(f => f.TimeToCompleteSec.HasValue).Average(f => (double)f.TimeToCompleteSec!.Value)
                    : null))
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.FinalStatus)
            .ToList();

        return Result<OrdersReportResponse>.Success(new OrdersReportResponse(
            request.FromUtc, request.ToUtc, facts.Count, cells));
    }
}
