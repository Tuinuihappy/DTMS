using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderTimeline;

public record TimelineEntryDto(Guid Id, string EventType, string? Details, string? ActorId, DateTime OccurredAt);

public record GetOrderTimelineQuery(Guid OrderId) : IQuery<List<TimelineEntryDto>>;

public class GetOrderTimelineQueryHandler : IQueryHandler<GetOrderTimelineQuery, List<TimelineEntryDto>>
{
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IOrderAmendmentRepository _amendmentRepo;

    public GetOrderTimelineQueryHandler(IOrderAuditEventRepository auditRepo, IOrderAmendmentRepository amendmentRepo)
    {
        _auditRepo = auditRepo;
        _amendmentRepo = amendmentRepo;
    }

    public async Task<Result<List<TimelineEntryDto>>> Handle(GetOrderTimelineQuery request, CancellationToken cancellationToken)
    {
        var auditEvents = await _auditRepo.GetByOrderAsync(request.OrderId, cancellationToken);
        var amendments = await _amendmentRepo.GetByOrderAsync(request.OrderId, cancellationToken);

        var timeline = auditEvents
            .Select(e => new TimelineEntryDto(e.Id, e.EventType, e.Details, e.ActorId, e.OccurredAt))
            .Concat(amendments.Select(a => new TimelineEntryDto(
                a.Id, $"Amendment:{a.Type}", a.Reason, a.AmendedBy, a.AmendedAt)))
            .OrderBy(e => e.OccurredAt)
            .ToList();

        return Result<List<TimelineEntryDto>>.Success(timeline);
    }
}
