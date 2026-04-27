using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ResolveException;

public class ResolveExceptionCommandHandler : ICommandHandler<ResolveExceptionCommand>
{
    private readonly ITripRepository _tripRepository;

    public ResolveExceptionCommandHandler(ITripRepository tripRepository) => _tripRepository = tripRepository;

    public async Task<Result> Handle(ResolveExceptionCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            trip.ResolveException(request.ExceptionId, request.Resolution, request.ResolvedBy);
            await _tripRepository.UpdateAsync(trip, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
