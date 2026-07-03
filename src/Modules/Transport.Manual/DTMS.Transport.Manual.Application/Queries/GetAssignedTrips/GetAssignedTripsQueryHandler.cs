using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Queries.GetAssignedTrips;

internal sealed class GetAssignedTripsQueryHandler : IQueryHandler<GetAssignedTripsQuery, IReadOnlyList<AssignedTripDto>>
{
    private readonly IManualTripExtensionRepository _extensions;
    public GetAssignedTripsQueryHandler(IManualTripExtensionRepository extensions) => _extensions = extensions;

    public async Task<Result<IReadOnlyList<AssignedTripDto>>> Handle(GetAssignedTripsQuery request, CancellationToken cancellationToken)
    {
        var extensions = await _extensions.GetByOperatorIdAsync(request.OperatorId, cancellationToken);
        var dtos = extensions.Select(e => new AssignedTripDto(
            TripId: e.TripId,
            AssignedAt: e.AssignedAt,
            AcknowledgedAt: e.AcknowledgedAt,
            PickedUpAt: e.PickedUpAt,
            DroppedAt: e.DroppedAt,
            PickupDeadline: e.PickupDeadline,
            DropDeadline: e.DropDeadline,
            PickupOverrideUsed: e.PickupGeofenceOverrideId.HasValue,
            DropOverrideUsed: e.DropGeofenceOverrideId.HasValue)).ToList();
        return Result<IReadOnlyList<AssignedTripDto>>.Success(dtos);
    }
}
