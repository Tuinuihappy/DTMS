using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Queries.GetAssignedTrips;

internal sealed class GetAssignedTripsQueryHandler : IQueryHandler<GetAssignedTripsQuery, IReadOnlyList<AssignedTripDto>>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;

    public GetAssignedTripsQueryHandler(IManualTripExtensionRepository extensions, ITripRepository trips)
    {
        _extensions = extensions;
        _trips = trips;
    }

    public async Task<Result<IReadOnlyList<AssignedTripDto>>> Handle(GetAssignedTripsQuery request, CancellationToken cancellationToken)
    {
        var extensions = await _extensions.GetByOperatorIdAsync(request.OperatorId, cancellationToken);
        // The extension only records what the operator did; a dispatcher
        // cancel (or an order cancel cascading to the trip) lands on Trip
        // alone. Read the live status so the app can stop offering
        // pickup/drop on a trip that is already over.
        var statuses = await _trips.GetStatusesAsync(
            extensions.Select(e => e.TripId).ToList(), cancellationToken);
        var dtos = extensions.Select(e => new AssignedTripDto(
            TripId: e.TripId,
            TripStatus: statuses.TryGetValue(e.TripId, out var s) ? s.ToString() : null,
            AssignedAt: e.AssignedAt,
            AcknowledgedAt: e.AcknowledgedAt,
            PickedUpAt: e.PickedUpAt,
            DroppedAt: e.DroppedAt,
            CompletedAt: e.CompletedAt,
            PickupDeadline: e.PickupDeadline,
            DropDeadline: e.DropDeadline,
            PickupOverrideUsed: e.PickupGeofenceOverrideId.HasValue,
            DropOverrideUsed: e.DropGeofenceOverrideId.HasValue)).ToList();
        return Result<IReadOnlyList<AssignedTripDto>>.Success(dtos);
    }
}
