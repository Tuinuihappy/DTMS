using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CapturePoD;

public class CapturePodCommandHandler : ICommandHandler<CapturePodCommand, Guid>
{
    private readonly ITripRepository _tripRepository;
    private readonly IEventBus _eventBus;

    public CapturePodCommandHandler(ITripRepository tripRepository, IEventBus eventBus)
    {
        _tripRepository = tripRepository;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(CapturePodCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result<Guid>.Failure($"Trip {request.TripId} not found.");

        var pod = trip.CaptureProofOfDelivery(
            request.StopId, request.PhotoUrl, request.SignatureData, request.ScannedIds, request.Notes);

        await _tripRepository.UpdateAsync(trip, cancellationToken);

        await _eventBus.PublishAsync(new PodCapturedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, trip.Id, request.StopId), cancellationToken);

        return Result<Guid>.Success(pod.Id);
    }
}
