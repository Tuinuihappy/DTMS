using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Commands.RecordDrop;

internal sealed class RecordDropCommandHandler : ICommandHandler<RecordDropCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;
    private readonly IWarehouseRepository _warehouses;
    private readonly IGeofenceOverrideRequestRepository _overrides;

    public RecordDropCommandHandler(
        IManualTripExtensionRepository extensions,
        ITripRepository trips,
        IWarehouseRepository warehouses,
        IGeofenceOverrideRequestRepository overrides)
    {
        _extensions = extensions;
        _trips = trips;
        _warehouses = warehouses;
        _overrides = overrides;
    }

    public async Task<Result> Handle(RecordDropCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension.");
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure("Trip is assigned to a different operator.");

        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip?.DropWarehouseId is null)
            return Result.Failure($"Trip {request.TripId} has no drop warehouse.");

        var warehouse = await _warehouses.GetByIdAsync(trip.DropWarehouseId.Value, cancellationToken);
        if (warehouse is null)
            return Result.Failure($"Drop warehouse {trip.DropWarehouseId} not found.");

        var check = GeofenceCalculator.Check(
            request.ReportedLat, request.ReportedLng,
            warehouse.Location.Lat, warehouse.Location.Lng,
            warehouse.GeofenceRadiusM);

        Guid? overrideId = null;
        if (!check.IsInside)
        {
            var approvedOverride = await _overrides.GetApprovedForTripLegAsync(
                trip.Id, request.OperatorId, trip.DropWarehouseId.Value, cancellationToken);
            if (approvedOverride is null)
            {
                return Result.Failure(
                    $"GEOFENCE_REJECTED: {check.OvershootM:F0}m outside warehouse geofence " +
                    $"(radius {check.RadiusM}m). Submit an override request first.");
            }
            overrideId = approvedOverride.Id;
        }

        var firstDrop = !ext.DroppedAt.HasValue;
        ext.MarkDropped(podKey: request.PodKey, overrideId: overrideId);
        await _extensions.SaveChangesAsync(cancellationToken);

        // Mirror the vendor-drop event so the DeliveryOrder-side
        // TripDropCompletedConsumer projects Item.Status PickedUp →
        // DroppedOff (or Delivered if RequiresDropPod=false). AMR fires
        // this from the RIOT3 webhook; Manual from the operator's drop
        // action. Idempotent on double-tap via the firstDrop guard.
        if (firstDrop)
        {
            trip.MarkVendorDropCompleted();
            await _trips.UpdateAsync(trip, cancellationToken);
        }
        return Result.Success();
    }
}
