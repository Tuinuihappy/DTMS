using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.RecordPickup;

// Per ADR-016 — server-strict geofence check. If the operator's GPS
// reports outside Warehouse.GeofenceRadiusM, the pickup is rejected
// UNLESS an approved GeofenceOverrideRequest exists for this trip leg.
internal sealed class RecordPickupCommandHandler : ICommandHandler<RecordPickupCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;
    private readonly IWarehouseRepository _warehouses;
    private readonly IGeofenceOverrideRequestRepository _overrides;

    public RecordPickupCommandHandler(
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

    public async Task<Result> Handle(RecordPickupCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension.");
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure("Trip is assigned to a different operator.");

        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip?.PickupWarehouseId is null)
            return Result.Failure($"Trip {request.TripId} has no pickup warehouse.");

        var warehouse = await _warehouses.GetByIdAsync(trip.PickupWarehouseId.Value, cancellationToken);
        if (warehouse is null)
            return Result.Failure($"Pickup warehouse {trip.PickupWarehouseId} not found.");

        var check = GeofenceCalculator.Check(
            request.ReportedLat, request.ReportedLng,
            warehouse.Location.Lat, warehouse.Location.Lng,
            warehouse.GeofenceRadiusM);

        Guid? overrideId = null;
        if (!check.IsInside)
        {
            // Look for an already-approved override for this exact leg.
            var approvedOverride = await _overrides.GetApprovedForTripLegAsync(
                trip.Id, request.OperatorId, trip.PickupWarehouseId.Value, cancellationToken);
            if (approvedOverride is null)
            {
                return Result.Failure(
                    $"GEOFENCE_REJECTED: {check.OvershootM:F0}m outside warehouse geofence " +
                    $"(radius {check.RadiusM}m). Submit an override request first.");
            }
            overrideId = approvedOverride.Id;
        }

        var firstPickup = !ext.PickedUpAt.HasValue;
        ext.MarkPickedUp(podKey: request.PodKey, overrideId: overrideId);
        await _extensions.SaveChangesAsync(cancellationToken);

        // Mirror the vendor-pickup event so the DeliveryOrder-side
        // TripPickupCompletedConsumer projects Item.Status PENDING →
        // PickedUp. AMR fires this from the RIOT3 webhook; Manual fires
        // it from the operator's pickup action. Skip on idempotent
        // double-tap so we don't re-emit the integration event.
        if (firstPickup)
        {
            trip.MarkVendorPickedUp();
            await _trips.UpdateAsync(trip, cancellationToken);
        }
        return Result.Success();
    }
}
