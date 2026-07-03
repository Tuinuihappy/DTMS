using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Options;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using DTMS.Wms.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace DTMS.Transport.Manual.Application.Commands.SubmitGeofenceOverride;

// WMS PR-3b — geofence override request is now scoped to a WMS location
// (previously scoped to a Warehouse row). Operator submits when their
// GPS falls outside the configured radius around the trip's pickup or
// drop WMS location.
internal sealed class SubmitGeofenceOverrideCommandHandler : ICommandHandler<SubmitGeofenceOverrideCommand, Guid>
{
    // Per ADR-016 — dispatcher has 15 minutes to decide before the
    // request auto-expires. Made an internal constant so dispatcher
    // console can echo the same window in its UI.
    public static readonly TimeSpan DefaultExpiryWindow = TimeSpan.FromMinutes(15);

    private readonly IGeofenceOverrideRequestRepository _overrides;
    private readonly ITripRepository _trips;
    private readonly IWmsLocationRepository _wmsLocations;
    private readonly RecordDropGeofenceOptions _geofenceOptions;

    public SubmitGeofenceOverrideCommandHandler(
        IGeofenceOverrideRequestRepository overrides,
        ITripRepository trips,
        IWmsLocationRepository wmsLocations,
        IOptions<RecordDropGeofenceOptions> geofenceOptions)
    {
        _overrides = overrides;
        _trips = trips;
        _wmsLocations = wmsLocations;
        _geofenceOptions = geofenceOptions.Value;
    }

    public async Task<Result<Guid>> Handle(SubmitGeofenceOverrideCommand request, CancellationToken cancellationToken)
    {
        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<Guid>.Failure($"Trip {request.TripId} not found.");

        // The WMS location must be one of trip's two legs — operator
        // can't request an override for an unrelated location.
        if (trip.PickupWmsLocationId != request.ExpectedWmsLocationId &&
            trip.DropWmsLocationId != request.ExpectedWmsLocationId)
        {
            return Result<Guid>.Failure(
                "Override WMS location must match the trip's pickup or drop location.");
        }

        var loc = await _wmsLocations.GetByIdAsync(request.ExpectedWmsLocationId, cancellationToken);
        if (loc is null)
            return Result<Guid>.Failure($"WMS location {request.ExpectedWmsLocationId} not found in snapshot.");
        if (loc.Latitude is null || loc.Longitude is null)
            return Result<Guid>.Failure(
                $"WMS location '{loc.LocationCode}' has no GPS coordinates — geofence check impossible.");

        // Compute the actual overshoot so the dispatcher's review UI
        // can render "operator is 250m from the geofence" without
        // re-computing.
        var check = GeofenceCalculator.Check(
            request.ReportedLat, request.ReportedLng,
            loc.Latitude.Value, loc.Longitude.Value,
            (int)_geofenceOptions.DefaultRadiusM);

        // Distance argument to Submit() must be strictly positive; if
        // the operator was actually inside (rare race — geofence check
        // raced with a GPS update), fall back to 1m so domain factory
        // doesn't throw. Dispatcher will see "1m" and likely deny.
        var distanceForRecord = check.IsInside ? 1.0 : check.DistanceM;

        var record = GeofenceOverrideRequest.Submit(
            operatorId: request.OperatorId,
            tripId: request.TripId,
            expectedWmsLocationId: request.ExpectedWmsLocationId,
            reportedLat: request.ReportedLat,
            reportedLng: request.ReportedLng,
            distanceFromGeofenceM: distanceForRecord,
            reason: request.Reason,
            photoUrl: request.PhotoUrl,
            expiresIn: DefaultExpiryWindow);

        await _overrides.AddAsync(record, cancellationToken);
        await _overrides.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(record.Id);
    }
}
