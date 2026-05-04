using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CapturePoD;

public class CapturePodCommandHandler : ICommandHandler<CapturePodCommand, Guid>
{
    private readonly ITripRepository _tripRepository;

    public CapturePodCommandHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result<Guid>> Handle(CapturePodCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result<Guid>.Failure($"Trip {request.TripId} not found.");

        var pod = trip.CaptureProofOfDelivery(
            request.StopId, request.PhotoUrl, request.SignatureData, request.ScannedIds, request.Notes);

        await _tripRepository.UpdateAsync(trip, cancellationToken);

        return Result<Guid>.Success(pod.Id);
    }
}
