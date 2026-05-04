using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;

public class RaiseExceptionCommandHandler : ICommandHandler<RaiseExceptionCommand, Guid>
{
    private readonly ITripRepository _tripRepository;

    public RaiseExceptionCommandHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result<Guid>> Handle(RaiseExceptionCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result<Guid>.Failure($"Trip {request.TripId} not found.");

        var exception = trip.RaiseException(request.Code, request.Severity, request.Detail);
        await _tripRepository.UpdateAsync(trip, cancellationToken);

        return Result<Guid>.Success(exception.Id);
    }
}
