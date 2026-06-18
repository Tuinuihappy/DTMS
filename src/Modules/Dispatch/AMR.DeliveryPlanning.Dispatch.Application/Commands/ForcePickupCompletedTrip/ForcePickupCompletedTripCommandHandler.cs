using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ForcePickupCompletedTrip;

public class ForcePickupCompletedTripCommandHandler : ICommandHandler<ForcePickupCompletedTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<ForcePickupCompletedTripCommandHandler> _logger;

    public ForcePickupCompletedTripCommandHandler(
        ITripRepository tripRepository,
        ILogger<ForcePickupCompletedTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(ForcePickupCompletedTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null) return Result.Failure($"Trip {request.TripId} not found.");

        // MarkVendorPickedUp silently bails outside InProgress — surface
        // that as a clear API error rather than a confusing 200/no-op.
        if (trip.Status is not TripStatus.InProgress)
            return Result.Failure(
                $"Cannot force-pickup a trip in {trip.Status} status. " +
                "Only InProgress trips can be force-pickup-completed.");

        var alreadyPicked = trip.Events.Any(e => e.EventType == "VendorPickupCompleted");

        trip.MarkVendorPickedUp();
        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForcePickup] Trip {TripId} force-pickup-completed by {Actor} (upperKey {UpperKey}, alreadyPicked={Already}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)", alreadyPicked, request.Reason);
        return Result.Success();
    }
}
