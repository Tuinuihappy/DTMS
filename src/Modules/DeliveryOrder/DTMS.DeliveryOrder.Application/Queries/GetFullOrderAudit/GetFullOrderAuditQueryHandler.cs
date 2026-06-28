using DTMS.DeliveryOrder.Application.Projections;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetFullOrderAudit;

/// <summary>
/// Phase P2 — Transparent swap of the legacy 4-source UNION query to a
/// single indexed read against <c>deliveryorder.OrderActivity</c>. The
/// API contract (FullOrderAuditDto + FullAuditEntryDto) is unchanged, so
/// the existing <c>&lt;FullAuditLog /&gt;</c> frontend component works
/// untouched.
///
/// Category → Source mapping preserves the legacy taxonomy:
///   OrderLifecycle   → "Order"
///   Amendment        → "Amendment"
///   TripExecution    → "TripExecution"
///   TripRetry        → "TripRetry"
///   Pod              → "Order"  (legacy bucket)
///   OmsNotify        → "Order"  (legacy bucket)
///   anything else    → "Order"
///
/// Backfill SQL seeded historical rows from the 4 original sources so
/// existing orders show the same audit history they did before the swap.
/// </summary>
public class GetFullOrderAuditQueryHandler : IQueryHandler<GetFullOrderAuditQuery, FullOrderAuditDto>
{
    private readonly IOrderActivityReadRepository _activityRepo;

    public GetFullOrderAuditQueryHandler(IOrderActivityReadRepository activityRepo)
        => _activityRepo = activityRepo;

    public async Task<Result<FullOrderAuditDto>> Handle(GetFullOrderAuditQuery request, CancellationToken cancellationToken)
    {
        var entries = await _activityRepo.GetForOrderAsync(request.OrderId, cancellationToken);

        var dtos = entries
            .Select(e => new FullAuditEntryDto(
                Id: e.Id,
                Source: MapCategoryToSource(e.Category),
                EventType: e.EventType,
                Details: e.Details,
                ActorId: e.ActorId,
                OccurredAt: e.OccurredAt,
                RelatedTripId: e.RelatedTripId,
                AttemptNumber: e.AttemptNumber,
                Channel: e.Channel,
                DisplayName: e.DisplayName))
            .ToList();

        return Result<FullOrderAuditDto>.Success(
            new FullOrderAuditDto(request.OrderId, dtos.Count, dtos));
    }

    private static string MapCategoryToSource(string category) => category switch
    {
        "OrderLifecycle" => "Order",
        "Amendment"      => "Amendment",
        "TripExecution"  => "TripExecution",
        "TripRetry"      => "TripRetry",
        // Pod / OmsNotify / unknown — bucket under "Order" so the UI's
        // legacy color palette + filter logic still works.
        _                => "Order",
    };
}
