using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.ForceStartTrip;

public class ForceStartTripCommandHandler : ICommandHandler<ForceStartTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITripItemSnapshotProvider _itemSnapshotProvider;
    private readonly ILogger<ForceStartTripCommandHandler> _logger;

    public ForceStartTripCommandHandler(
        ITripRepository tripRepository,
        ITripItemSnapshotProvider itemSnapshotProvider,
        ILogger<ForceStartTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _itemSnapshotProvider = itemSnapshotProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(ForceStartTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null) return Result.Failure($"Trip {request.TripId} not found.");

        // Trip.MarkVendorStarted is a no-op outside Status=Created, so a
        // strict gate here makes the failure mode legible instead of a
        // silent "API returned 200 but nothing happened" surprise.
        if (trip.Status is not TripStatus.Created)
            return Result.Failure(
                $"Cannot force-start a trip in {trip.Status} status. " +
                "Only Created trips can be force-started.");

        var items = await _itemSnapshotProvider.GetForTripAsync(trip.Id, cancellationToken);
        try
        {
            trip.MarkVendorStarted(
                vehicleId: null,
                vendorVehicleKey: request.VendorVehicleKey,
                vendorVehicleName: request.VendorVehicleName,
                items: items);
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForceStart] Trip {TripId} force-started by {Actor} (upperKey {UpperKey}, vendor='{VehKey}'/'{VehName}', items={ItemCount}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)",
            request.VendorVehicleKey ?? "(none)", request.VendorVehicleName ?? "(none)",
            items.Count, request.Reason);
        return Result.Success();
    }
}
