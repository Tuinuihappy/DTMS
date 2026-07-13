using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.ForcePickupCompletedTrip;

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

        // Fire-once: the vendor (or a prior force) already recorded pickup, so
        // MarkVendorPickedUp would be a no-op. Short-circuit before the pointless
        // save + event re-fire and tell the operator the state already holds.
        if (trip.VendorPickedUpAt is not null)
        {
            _logger.LogInformation(
                "[AdminForcePickup] Trip {TripId} already picked up at {At} (upperKey {UpperKey}) — force is a no-op.",
                trip.Id, trip.VendorPickedUpAt, trip.UpperKey ?? "(none)");
            return Result.Success();
        }

        trip.MarkVendorPickedUp();
        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForcePickup] Trip {TripId} force-pickup-completed by {Actor} (upperKey {UpperKey}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)", request.Reason);
        return Result.Success();
    }
}
