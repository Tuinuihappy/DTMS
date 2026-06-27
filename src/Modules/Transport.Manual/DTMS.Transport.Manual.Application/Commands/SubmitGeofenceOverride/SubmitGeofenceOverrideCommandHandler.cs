using DTMS.Dispatch.Domain.Repositories;
using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Commands.SubmitGeofenceOverride;

internal sealed class SubmitGeofenceOverrideCommandHandler : ICommandHandler<SubmitGeofenceOverrideCommand, Guid>
{
    // Per ADR-016 — dispatcher has 15 minutes to decide before the
    // request auto-expires. Made an internal constant so dispatcher
    // console can echo the same window in its UI.
    public static readonly TimeSpan DefaultExpiryWindow = TimeSpan.FromMinutes(15);

    private readonly IGeofenceOverrideRequestRepository _overrides;
    private readonly ITripRepository _trips;
    private readonly IWarehouseRepository _warehouses;

    public SubmitGeofenceOverrideCommandHandler(
        IGeofenceOverrideRequestRepository overrides,
        ITripRepository trips,
        IWarehouseRepository warehouses)
    {
        _overrides = overrides;
        _trips = trips;
        _warehouses = warehouses;
    }

    public async Task<Result<Guid>> Handle(SubmitGeofenceOverrideCommand request, CancellationToken cancellationToken)
    {
        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null)
            return Result<Guid>.Failure($"Trip {request.TripId} not found.");

        // The warehouse must be one of trip's two legs — operator can't
        // request override for an unrelated warehouse.
        if (trip.PickupWarehouseId != request.ExpectedWarehouseId &&
            trip.DropWarehouseId != request.ExpectedWarehouseId)
        {
            return Result<Guid>.Failure(
                "Override warehouse must match the trip's pickup or drop warehouse.");
        }

        var warehouse = await _warehouses.GetByIdAsync(request.ExpectedWarehouseId, cancellationToken);
        if (warehouse is null)
            return Result<Guid>.Failure($"Warehouse {request.ExpectedWarehouseId} not found.");

        // Compute the actual overshoot so the dispatcher's review UI
        // can render "operator is 250m from the geofence" without
        // re-computing.
        var check = GeofenceCalculator.Check(
            request.ReportedLat, request.ReportedLng,
            warehouse.Location.Lat, warehouse.Location.Lng,
            warehouse.GeofenceRadiusM);

        // Distance argument to Submit() must be strictly positive; if
        // the operator was actually inside (rare race — geofence check
        // raced with a GPS update), fall back to 1m so domain factory
        // doesn't throw. Dispatcher will see "1m" and likely deny.
        var distanceForRecord = check.IsInside ? 1.0 : check.DistanceM;

        var record = GeofenceOverrideRequest.Submit(
            operatorId: request.OperatorId,
            tripId: request.TripId,
            expectedWarehouseId: request.ExpectedWarehouseId,
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
