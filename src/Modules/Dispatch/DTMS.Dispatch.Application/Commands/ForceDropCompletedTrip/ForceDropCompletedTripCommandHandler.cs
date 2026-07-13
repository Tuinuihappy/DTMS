using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.ForceDropCompletedTrip;

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

        // Fire-once: the vendor (or a prior force) already recorded the drop, so
        // MarkVendorDropCompleted would be a no-op. Short-circuit before the
        // pointless save (and the extra RIOT3 POD lookup) and tell the operator
        // the state already holds.
        if (trip.VendorDroppedAt is not null)
        {
            _logger.LogInformation(
                "[AdminForceDrop] Trip {TripId} already dropped at {At} (upperKey {UpperKey}) — force is a no-op.",
                trip.Id, trip.VendorDroppedAt, trip.UpperKey ?? "(none)");
            return Result.Success();
        }

        // RequiresDropPod determines whether items land at Delivered (no POD)
        // or DroppedOff (POD pending) — same lookup the vendor webhook does
        // so downstream behaviour matches.
        var requiresDropPod = await _orderReader.GetRequiresDropPodAsync(
            trip.DeliveryOrderId, cancellationToken);

        trip.MarkVendorDropCompleted(requiresDropPod);
        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogWarning(
            "[AdminForceDrop] Trip {TripId} force-drop-completed by {Actor} (upperKey {UpperKey}, requiresDropPod={Pod}): {Reason}",
            trip.Id, request.TriggeredBy ?? "(unknown)", trip.UpperKey ?? "(none)",
            requiresDropPod?.ToString() ?? "(null)", request.Reason);
        return Result.Success();
    }
}
