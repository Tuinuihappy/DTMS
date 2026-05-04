using AMR.DeliveryPlanning.Dispatch.Application.Services;

using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;

public class CancelTripCommandHandler : ICommandHandler<CancelTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITaskDispatcher _taskDispatcher;

    public CancelTripCommandHandler(ITripRepository tripRepository, ITaskDispatcher taskDispatcher)
    {
        _tripRepository = tripRepository;
        _taskDispatcher = taskDispatcher;
    }

    public async Task<Result> Handle(CancelTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            var activeTask = trip.Tasks.FirstOrDefault(t =>
                t.Status == Domain.Enums.TaskStatus.Dispatched || t.Status == Domain.Enums.TaskStatus.InProgress);

            trip.Cancel(request.Reason);
            await _tripRepository.UpdateAsync(trip, cancellationToken);

            if (activeTask != null)
                await _taskDispatcher.CancelAsync(trip.VehicleId, activeTask.Id, cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
