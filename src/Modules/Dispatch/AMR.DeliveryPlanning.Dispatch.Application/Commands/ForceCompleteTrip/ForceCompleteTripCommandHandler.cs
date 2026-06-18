using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceCompleteTrip;

public class ForceCompleteTripCommandHandler : ICommandHandler<ForceCompleteTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderStatusReader _orderReader;
    private readonly ILogger<ForceCompleteTripCommandHandler> _logger;

    public ForceCompleteTripCommandHandler(
        ITripRepository tripRepository,
        IDeliveryOrderStatusReader orderReader,
        ILogger<ForceCompleteTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _orderReader = orderReader;
        _logger = logger;
    }

    public async Task<Result> Handle(ForceCompleteTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null) return Result.Failure($"Trip {request.TripId} not found.");

        // Restrict to in-flight states. Trip.MarkVendorCompleted is more
        // permissive (it also accepts Created), but force-completing a
        // Trip that never started is almost always operator error — refuse
        // it here and surface the real action (cancel + re-dispatch).
        if (trip.Status is not (TripStatus.InProgress or TripStatus.Paused))
            return Result.Failure(
                $"Cannot force-complete a trip in {trip.Status} status. " +
                "Only InProgress or Paused trips can be force-completed.");

        // When the drop sub-task webhook was dropped (the same root cause
        // we're already working around), VendorDropCompleted never fired —
        // which means TripDropCompletedIntegrationEvent never went out and
        // OMS never received the /arrived notification. Force-complete is
        // an "everything finished" override, so fill in that gap first so
        // the OMS notify chain runs end-to-end. Skips if the drop already
        // completed normally (idempotent on the consumer side too).
        var dropAlreadyCompleted = trip.Events.Any(e => e.EventType == "VendorDropCompleted");
        var droppedNow = false;
        if (!dropAlreadyCompleted)
        {
            var requiresDropPod = await _orderReader.GetRequiresDropPodAsync(
                trip.DeliveryOrderId, cancellationToken);
            try { trip.MarkVendorDropCompleted(requiresDropPod); droppedNow = true; }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    "[AdminForceComplete] Trip {TripId} drop-completion skipped: {Error}",
                    trip.Id, ex.Message);
            }
        }

        try { trip.MarkVendorCompleted(); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForceComplete] Trip {TripId} force-completed by {Actor} (upperKey {UpperKey}, syntheticDrop={Drop}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)", droppedNow, request.Reason);
        return Result.Success();
    }
}
