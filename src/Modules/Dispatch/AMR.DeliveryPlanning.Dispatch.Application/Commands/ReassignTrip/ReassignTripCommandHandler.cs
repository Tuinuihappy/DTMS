using AMR.DeliveryPlanning.Dispatch.Application.Services;

using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReassignTrip;

public class ReassignTripCommandHandler : ICommandHandler<ReassignTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITaskDispatcher _taskDispatcher;

    public ReassignTripCommandHandler(ITripRepository tripRepository, ITaskDispatcher taskDispatcher)
    {
        _tripRepository = tripRepository;
        _taskDispatcher = taskDispatcher;
    }

    public async Task<Result> Handle(ReassignTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            // Cancel current active task on old vehicle
            var activeTask = trip.Tasks.FirstOrDefault(t =>
                t.Status == Domain.Enums.TaskStatus.Dispatched || t.Status == Domain.Enums.TaskStatus.InProgress);

            if (activeTask != null)
                await _taskDispatcher.CancelAsync(trip.VehicleId, activeTask.Id, cancellationToken);

            var oldVehicleId = trip.VehicleId;
            trip.Reassign(request.NewVehicleId);
            await _tripRepository.UpdateAsync(trip, cancellationToken);

            // Re-dispatch first pending task to new vehicle
            var firstPending = trip.Tasks
                .OrderBy(t => t.SequenceOrder)
                .FirstOrDefault(t => t.Status == Domain.Enums.TaskStatus.Dispatched);

            if (firstPending != null)
                await _taskDispatcher.DispatchAsync(request.NewVehicleId, firstPending, cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
