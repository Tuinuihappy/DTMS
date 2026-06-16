using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripItems;

/// <summary>
/// BFF (Backend-for-Frontend) endpoint — embeds the parent Trip's
/// metadata alongside its bound items so the operator UI can render
/// the trip drawer's items tab with a single round-trip. Trip
/// metadata is a convenience snapshot mirrored from
/// <c>GET /trips/{id}/details</c>, which remains the source of truth
/// for the full operator view (including missions timeline + raw
/// vendor JSON). Items are backed by the dispatch.TripItems read
/// model materialized by <c>TripItemsProjector</c>.
/// </summary>
public record GetTripItemsQuery(Guid TripId) : IQuery<TripItemsResponse>;

public sealed record TripItemsResponse(
    Guid TripId,
    TripContextDto Trip,
    IReadOnlyList<TripItemDto> Items);

// Trip-level convenience snapshot embedded for BFF. Mirrors the subset
// of TripDetailsDto that the operator drawer's items tab consumes;
// missions/vendor raw blobs intentionally omitted (call /details for
// the full forensic view).
public sealed record TripContextDto(
    string Status,
    int AttemptNumber,
    string UpperKey,
    string? VendorOrderKey,
    string? VendorVehicleKey,
    string? VendorVehicleName,
    string? TemplateNameAtDispatch,
    int? PriorityAtDispatch,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureReason);

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
    string Status,
    string? TransportMode);
