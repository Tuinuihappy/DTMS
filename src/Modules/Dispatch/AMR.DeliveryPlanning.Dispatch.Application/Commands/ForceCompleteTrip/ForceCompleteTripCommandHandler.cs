using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceCompleteTrip;

public class ForceCompleteTripCommandHandler : ICommandHandler<ForceCompleteTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<ForceCompleteTripCommandHandler> _logger;

    public ForceCompleteTripCommandHandler(
        ITripRepository tripRepository,
        ILogger<ForceCompleteTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(ForceCompleteTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null) return Result.Failure($"Trip {request.TripId} not found.");

        // Restrict to in-flight states. Trip.MarkVendorCompleted is more
        // permissive (it also accepts Created), but force-completing a
        // Trip that never started is almost always operator error — refuse
        // it here and surface the real action (start it first).
        if (trip.Status is not (TripStatus.InProgress or TripStatus.Paused))
            return Result.Failure(
                $"Cannot force-complete a trip in {trip.Status} status. " +
                "Only InProgress or Paused trips can be force-completed.");

        try { trip.MarkVendorCompleted(); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForceComplete] Trip {TripId} force-completed by {Actor} (upperKey {UpperKey}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)", request.Reason);
        return Result.Success();
    }
}
