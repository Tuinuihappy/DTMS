using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripItems;

/// <summary>
/// Phase P5.3 — Returns the items currently bound to a trip plus each
/// item's owning Order context. Backed by the dispatch.TripItems read
/// model materialized by <c>TripItemsProjector</c>.
/// </summary>
public record GetTripItemsQuery(Guid TripId) : IQuery<TripItemsResponse>;

public sealed record TripItemsResponse(
    Guid TripId,
    int ItemCount,
    IReadOnlyList<TripItemDto> Items);

public sealed record TripItemDto(
    Guid ItemPk,
    string LotNo,
    int ItemSeq,
    string ItemStatus,
    string? PickupCode,
    string? DropCode,
    double? WeightKg,
    string? Description,
    TripItemQuantityDto? Quantity,
    OrderRefDto Order,
    DateTime BoundAt,
    DateTime LastEventAt);

// Mirrors DeliveryOrder's QuantityDto so the operator trip-items view
// surfaces the same { value, uom } shape clients already render
// elsewhere. Nullable on the parent — projector rows snapshotted
// before V1.3 carry no value/uom (both NULL in the row).
public sealed record TripItemQuantityDto(double Value, string Uom);

public sealed record OrderRefDto(
    Guid Id,
    string OrderRef,
    string Status);
