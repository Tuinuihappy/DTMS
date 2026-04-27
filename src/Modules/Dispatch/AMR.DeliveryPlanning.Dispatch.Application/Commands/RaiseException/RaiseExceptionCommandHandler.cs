using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;

public class RaiseExceptionCommandHandler : ICommandHandler<RaiseExceptionCommand, Guid>
{
    private readonly ITripRepository _tripRepository;
    private readonly IEventBus _eventBus;

    public RaiseExceptionCommandHandler(ITripRepository tripRepository, IEventBus eventBus)
    {
        _tripRepository = tripRepository;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(RaiseExceptionCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result<Guid>.Failure($"Trip {request.TripId} not found.");

        var exception = trip.RaiseException(request.Code, request.Severity, request.Detail);
        await _tripRepository.UpdateAsync(trip, cancellationToken);

        await _eventBus.PublishAsync(new ExceptionRaisedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow,
            trip.Id, trip.JobId, exception.Id,
            request.Code, request.Severity, request.Detail), cancellationToken);

        return Result<Guid>.Success(exception.Id);
    }
}
