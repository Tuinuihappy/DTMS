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
    // WMS PR-3 — Manual/Fleet items have null station Ids and match on
    // the WMS location pair instead. AMR callers leave the WMS pair null
    // (station-based) and the station branch fires.
    Guid? PickupWmsLocationId = null,
    Guid? DropWmsLocationId = null
) : ICommand<int>;
