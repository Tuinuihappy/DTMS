using DTMS.DeliveryOrder.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetOrderFunnel;

public class GetOrderFunnelQueryHandler
    : IQueryHandler<GetOrderFunnelQuery, OrderFunnelResponse>
{
    // Hard cap so a misconfigured client can't ask for years of buckets in
    // one shot. 90 days × 24 hours = 2160 rows — comfortably under EF's
    // tracking sweet spot, and plenty for a "trailing-quarter" view.
    private const int MaxWindowDays = 90;

    private readonly IOrderFunnelReadRepository _repo;

    public GetOrderFunnelQueryHandler(IOrderFunnelReadRepository repo) => _repo = repo;

    public async Task<Result<OrderFunnelResponse>> Handle(GetOrderFunnelQuery request, CancellationToken cancellationToken)
    {
        if (request.ToUtc <= request.FromUtc)
            return Result<OrderFunnelResponse>.Failure("ToUtc must be after FromUtc.");
        if ((request.ToUtc - request.FromUtc).TotalDays > MaxWindowDays)
            return Result<OrderFunnelResponse>.Failure($"Window must be <= {MaxWindowDays} days.");

        var buckets = await _repo.GetRangeAsync(request.FromUtc, request.ToUtc, cancellationToken);

        var dtos = buckets.Select(b => new OrderFunnelBucketDto(
            b.BucketHour, b.Confirmed, b.Dispatched, b.InProgress,
            b.Completed, b.PartiallyCompleted, b.Failed, b.Cancelled,
            b.Rejected, b.Held, b.Released)).ToList();

        var totals = new OrderFunnelTotals(
            Confirmed: dtos.Sum(b => b.Confirmed),
            Dispatched: dtos.Sum(b => b.Dispatched),
            InProgress: dtos.Sum(b => b.InProgress),
            Completed: dtos.Sum(b => b.Completed),
            PartiallyCompleted: dtos.Sum(b => b.PartiallyCompleted),
            Failed: dtos.Sum(b => b.Failed),
            Cancelled: dtos.Sum(b => b.Cancelled),
            Rejected: dtos.Sum(b => b.Rejected),
            Held: dtos.Sum(b => b.Held),
            Released: dtos.Sum(b => b.Released));

        var lastEventAt = dtos.Count > 0 ? dtos[^1].BucketHour : (DateTime?)null;

        return Result<OrderFunnelResponse>.Success(
            new OrderFunnelResponse(request.FromUtc, request.ToUtc, dtos, totals, lastEventAt));
    }
}
