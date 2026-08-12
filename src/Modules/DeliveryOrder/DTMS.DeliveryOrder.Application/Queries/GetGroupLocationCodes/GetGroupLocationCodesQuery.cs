using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetGroupLocationCodes;

/// <summary>
/// 2026-08 — resolves the SOURCE SYSTEM's own location code strings for one
/// dispatch group of an order, matched by whichever resolved-location pair the
/// caller has (stations for AMR, WMS locations for Manual/self-managed).
/// Called at the trip-creation funnels (DispatchOrderTemplateService +
/// Manual/SelfManaged strategies) so every new Trip is born carrying
/// Pickup/DropLocationCode — the codes that feed the pickedup/droppedoff
/// callbacks and must survive item unbinding on cancel.
///
/// <para>Deliberately soft: an unmatched pair returns (null, null), never a
/// failure — a trip without codes still works via the callback code's item
/// scan fallback; blocking dispatch over a display string would be wrong.</para>
/// </summary>
public record GetGroupLocationCodesQuery(
    Guid OrderId,
    Guid? PickupStationId = null,
    Guid? DropStationId = null,
    Guid? PickupWmsLocationId = null,
    Guid? DropWmsLocationId = null) : IQuery<GroupLocationCodes>;

public sealed record GroupLocationCodes(string? PickupCode, string? DropCode);

public class GetGroupLocationCodesQueryHandler
    : IQueryHandler<GetGroupLocationCodesQuery, GroupLocationCodes>
{
    private readonly IDeliveryOrderRepository _orders;

    public GetGroupLocationCodesQueryHandler(IDeliveryOrderRepository orders)
    {
        _orders = orders;
    }

    public async Task<Result<GroupLocationCodes>> Handle(
        GetGroupLocationCodesQuery request, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsNoTrackingAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<GroupLocationCodes>.Success(new GroupLocationCodes(null, null));

        // Match by the resolved pair the caller holds — the same key the
        // dispatch grouping used, so the hit is exactly this trip's group.
        var item = order.Items.FirstOrDefault(i =>
            (request.PickupStationId is not null
                && i.PickupStationId == request.PickupStationId
                && i.DropStationId == request.DropStationId)
            || (request.PickupWmsLocationId is not null
                && i.PickupWmsLocationId == request.PickupWmsLocationId
                && i.DropWmsLocationId == request.DropWmsLocationId));

        return Result<GroupLocationCodes>.Success(item is null
            ? new GroupLocationCodes(null, null)
            : new GroupLocationCodes(
                string.IsNullOrWhiteSpace(item.PickupLocationCode) ? null : item.PickupLocationCode,
                string.IsNullOrWhiteSpace(item.DropLocationCode) ? null : item.DropLocationCode));
    }
}
