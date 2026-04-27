using AMR.DeliveryPlanning.Dispatch.Application.Services;

using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.PauseTrip;

public class PauseTripCommandHandler : ICommandHandler<PauseTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITaskDispatcher _taskDispatcher;

    public PauseTripCommandHandler(ITripRepository tripRepository, ITaskDispatcher taskDispatcher)
    {
        _tripRepository = tripRepository;
        _taskDispatcher = taskDispatcher;
    }

    public async Task<Result> Handle(PauseTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            var dispatchedTask = trip.Tasks.FirstOrDefault(t => t.Status == Domain.Enums.TaskStatus.Dispatched || t.Status == Domain.Enums.TaskStatus.InProgress);

            trip.Pause();
            await _tripRepository.UpdateAsync(trip, cancellationToken);

            if (dispatchedTask != null)
                await _taskDispatcher.PauseAsync(trip.VehicleId, dispatchedTask.Id, cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
