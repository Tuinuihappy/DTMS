using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;

public class DispatchTripCommandHandler : ICommandHandler<DispatchTripCommand, Guid>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITaskDispatcher _taskDispatcher;
    private readonly ILogger<DispatchTripCommandHandler> _logger;

    public DispatchTripCommandHandler(
        ITripRepository tripRepository,
        ITaskDispatcher taskDispatcher,
        ILogger<DispatchTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _taskDispatcher = taskDispatcher;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(DispatchTripCommand request, CancellationToken cancellationToken)
    {
        var trip = new Trip(request.JobId, request.VehicleId);

        var legs = request.Legs.OrderBy(l => l.SequenceOrder).ToList();

        if (legs.Count == 0)
        {
            // Fallback for legacy calls with no legs
            return Result<Guid>.Failure("No legs provided for trip dispatch.");
        }

        int taskSeq = 1;
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            bool isFirstLeg = i == 0;
            bool isLastLeg = i == legs.Count - 1;

            // Move to the from-station of this leg (pickup)
            trip.AddTask(TaskType.Move, taskSeq++, leg.FromStationId);

            // Lift at pickup on first leg (or every pickup stop)
            if (isFirstLeg || legs.Count == 1)
                trip.AddTask(TaskType.Lift, taskSeq++, leg.FromStationId);

            // Move to destination of this leg
            trip.AddTask(TaskType.Move, taskSeq++, leg.ToStationId);

            // Drop at final destination
            if (isLastLeg)
                trip.AddTask(TaskType.Drop, taskSeq++, leg.ToStationId);
        }

        trip.Start();

        await _tripRepository.AddAsync(trip, cancellationToken);

        // Send first dispatched task to vendor
        var firstDispatchedTask = trip.Tasks
            .OrderBy(t => t.SequenceOrder)
            .FirstOrDefault(t => t.Status == Domain.Enums.TaskStatus.Dispatched);

        if (firstDispatchedTask != null)
        {
            try
            {
                await _taskDispatcher.DispatchAsync(request.VehicleId, firstDispatchedTask, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send first task {TaskId} to vendor for trip {TripId}", firstDispatchedTask.Id, trip.Id);
            }
        }

        return Result<Guid>.Success(trip.Id);
    }
}
