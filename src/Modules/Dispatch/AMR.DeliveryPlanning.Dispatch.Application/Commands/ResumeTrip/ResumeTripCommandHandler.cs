using AMR.DeliveryPlanning.Dispatch.Application.Services;

using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ResumeTrip;

public class ResumeTripCommandHandler : ICommandHandler<ResumeTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITaskDispatcher _taskDispatcher;

    public ResumeTripCommandHandler(ITripRepository tripRepository, ITaskDispatcher taskDispatcher)
    {
        _tripRepository = tripRepository;
        _taskDispatcher = taskDispatcher;
    }

    public async Task<Result> Handle(ResumeTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            trip.Resume();
            await _tripRepository.UpdateAsync(trip, cancellationToken);

            // Re-dispatch the next pending/paused task to vendor
            var nextTask = trip.Tasks
                .OrderBy(t => t.SequenceOrder)
                .FirstOrDefault(t => t.Status == Domain.Enums.TaskStatus.Dispatched || t.Status == Domain.Enums.TaskStatus.Pending);

            if (nextTask != null)
                await _taskDispatcher.ResumeAsync(trip.VehicleId, nextTask.Id, cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
