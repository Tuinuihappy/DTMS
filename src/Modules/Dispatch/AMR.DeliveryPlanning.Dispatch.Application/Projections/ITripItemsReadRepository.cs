namespace AMR.DeliveryPlanning.Dispatch.Application.Projections;

/// <summary>
/// Phase P5.3 — Read-side abstraction over dispatch.TripItems. Backs the
/// GET /api/v1/dispatch/trips/{id}/items endpoint. Kept tiny so the query
/// handler stays thin.
/// </summary>
public interface ITripItemsReadRepository
{
    Task<IReadOnlyList<TripItemReadModel>> GetByTripAsync(Guid tripId, CancellationToken cancellationToken = default);
}

public sealed record TripItemReadModel(
    Guid TripId,
    Guid ItemPk,
    Guid DeliveryOrderId,
    string OrderRef,
    string OrderStatus,
    string LotNo,
    int ItemSeq,
    string ItemStatus,
    string? PickupCode,
    string? DropCode,
    double? WeightKg,
    DateTime BoundAt,
    DateTime LastEventAt);
