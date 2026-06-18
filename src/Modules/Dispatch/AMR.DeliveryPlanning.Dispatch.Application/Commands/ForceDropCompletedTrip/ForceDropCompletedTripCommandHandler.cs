using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceDropCompletedTrip;

public class ForceDropCompletedTripCommandHandler : ICommandHandler<ForceDropCompletedTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly IDeliveryOrderStatusReader _orderReader;
    private readonly ILogger<ForceDropCompletedTripCommandHandler> _logger;

    public ForceDropCompletedTripCommandHandler(
        ITripRepository tripRepository,
        IDeliveryOrderStatusReader orderReader,
        ILogger<ForceDropCompletedTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _orderReader = orderReader;
        _logger = logger;
    }

    public async Task<Result> Handle(ForceDropCompletedTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null) return Result.Failure($"Trip {request.TripId} not found.");

        if (trip.Status is not TripStatus.InProgress)
            return Result.Failure(
                $"Cannot force-drop a trip in {trip.Status} status. " +
                "Only InProgress trips can be force-drop-completed.");

        var alreadyDropped = trip.Events.Any(e => e.EventType == "VendorDropCompleted");

        // RequiresDropPod determines whether items land at Delivered (no POD)
        // or DroppedOff (POD pending) — same lookup the vendor webhook does
        // so downstream behaviour matches.
        var requiresDropPod = await _orderReader.GetRequiresDropPodAsync(
            trip.DeliveryOrderId, cancellationToken);

        trip.MarkVendorDropCompleted(requiresDropPod);
        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForceDrop] Trip {TripId} force-drop-completed by {Actor} (upperKey {UpperKey}, requiresDropPod={Pod}, alreadyDropped={Already}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)",
            requiresDropPod?.ToString() ?? "(null)", alreadyDropped, request.Reason);
        return Result.Success();
    }
}
