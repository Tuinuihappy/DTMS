using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Commands.AssignItemsToTrip;

/// <summary>
/// Bind every item of a delivery order whose (PickupStationId,
/// DropStationId) pair matches the route to the given Trip. Issued by
/// the dispatcher immediately after a Trip is created (and again at
/// retry time so the new attempt becomes the authoritative owner).
/// Returns the number of items bound; 0 means no matching items —
/// which usually indicates an inconsistency the dispatcher should log.
/// </summary>
public record AssignItemsToTripCommand(
    Guid OrderId,
    Guid TripId,
    int AttemptNumber,
    Guid PickupStationId,
    Guid DropStationId,
    // Phase 4.6 follow-up — Manual / Fleet items have null station Ids
    // (Phase 2.5 ADR-002). Caller passes warehouse Ids so the matching
    // logic can fall back to the warehouse pair when the station pair
    // is unusable (both sides empty Guid). AMR callers leave these null
    // and the station-pair path keeps working.
    Guid? PickupWarehouseId = null,
    Guid? DropWarehouseId = null
) : ICommand<int>;
