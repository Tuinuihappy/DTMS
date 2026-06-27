using DTMS.DeliveryOrder.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetOrderStatusHistory;

public class GetOrderStatusHistoryQueryHandler
    : IQueryHandler<GetOrderStatusHistoryQuery, OrderStatusHistoryResponse>
{
    private readonly IOrderStatusHistoryReadRepository _repository;

    public GetOrderStatusHistoryQueryHandler(IOrderStatusHistoryReadRepository repository)
        => _repository = repository;

    public async Task<Result<OrderStatusHistoryResponse>> Handle(
        GetOrderStatusHistoryQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repository.GetForOrderAsync(request.OrderId, cancellationToken);

        var dtos = rows
            .Select(r => new OrderStatusHistoryEntryDto(
                r.EventId, r.FromStatus, r.ToStatus, r.OccurredAt, r.Reason))
            .ToList();

        var lastEventAt = dtos.Count > 0 ? dtos[0].OccurredAt : (DateTime?)null;

        return Result<OrderStatusHistoryResponse>.Success(
            new OrderStatusHistoryResponse(request.OrderId, dtos, lastEventAt));
    }
}
