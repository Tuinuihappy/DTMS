using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Options;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Repositories;
using DTMS.Wms.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace DTMS.Transport.Manual.Application.Commands.RecordPickup;

// Per ADR-016 / WMS PR-3 — server-strict geofence check against the
// trip's pickup WMS location. If the operator's GPS is outside the
// configured radius (Wms:Geofence:DefaultRadiusM), the pickup is
// rejected UNLESS an approved GeofenceOverrideRequest exists for this
// trip leg.
internal sealed class RecordPickupCommandHandler : ICommandHandler<RecordPickupCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;
    private readonly IWmsLocationRepository _wmsLocations;
    private readonly IGeofenceOverrideRequestRepository _overrides;
    private readonly RecordDropGeofenceOptions _geofenceOptions;

    public RecordPickupCommandHandler(
        IManualTripExtensionRepository extensions,
        ITripRepository trips,
        IWmsLocationRepository wmsLocations,
        IGeofenceOverrideRequestRepository overrides,
        IOptions<RecordDropGeofenceOptions> geofenceOptions)
    {
        _extensions = extensions;
        _trips = trips;
        _wmsLocations = wmsLocations;
        _overrides = overrides;
        _geofenceOptions = geofenceOptions.Value;
    }

    public async Task<Result> Handle(RecordPickupCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension.");
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure("Trip is assigned to a different operator.");

        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result.Failure($"Trip {request.TripId} not found.");

        // Geofence enforcement is opt-out per Wms:Geofence:Enabled. When
        // disabled, manual pickup needs no GPS at all and we skip the WMS
        // location lookup + distance check. When enabled, coordinates are
        // mandatory — otherwise an operator could bypass the fence by simply
        // omitting them.
        Guid? overrideId = null;
        if (_geofenceOptions.Enabled)
        {
            if (request.ReportedLat is null || request.ReportedLng is null)
                return Result.Failure(
                    "GEOFENCE_REQUIRED: operator GPS coordinates are required for pickup.");

            if (trip.PickupWmsLocationId is null)
                return Result.Failure($"Trip {request.TripId} has no pickup WMS location.");

            var loc = await _wmsLocations.GetByIdAsync(trip.PickupWmsLocationId.Value, cancellationToken);
            if (loc is null)
                return Result.Failure($"Pickup WMS location {trip.PickupWmsLocationId} not found in snapshot.");
            if (loc.Latitude is null || loc.Longitude is null)
                return Result.Failure(
                    $"Pickup WMS location '{loc.LocationCode}' has no GPS coordinates — geofence check impossible.");

            var check = GeofenceCalculator.Check(
                request.ReportedLat.Value, request.ReportedLng.Value,
                loc.Latitude.Value, loc.Longitude.Value,
                (int)_geofenceOptions.DefaultRadiusM);

            if (!check.IsInside)
            {
                // Look for an already-approved override for this exact leg.
                var approvedOverride = await _overrides.GetApprovedForTripLegAsync(
                    trip.Id, request.OperatorId, trip.PickupWmsLocationId.Value, cancellationToken);
                if (approvedOverride is null)
                {
                    return Result.Failure(
                        $"GEOFENCE_REJECTED: {check.OvershootM:F0}m outside WMS location geofence " +
                        $"(radius {check.RadiusM}m). Submit an override request first.");
                }
                overrideId = approvedOverride.Id;
            }
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
