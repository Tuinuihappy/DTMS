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
        var trip = new Trip(request.JobId, request.DeliveryOrderId, request.VehicleId);

        var legs = request.Legs.OrderBy(l => l.SequenceOrder).ToList();

        if (legs.Count == 0)
            return Result<Guid>.Failure("No legs provided for trip dispatch.");

        if (legs.Any(l => l.FromStationId == Guid.Empty || l.ToStationId == Guid.Empty))
            return Result<Guid>.Failure(
                "DispatchTrip received a leg with Guid.Empty station — Planning must filter synthetic legs before emitting.");

        int taskSeq = 1;
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            bool isFirstLeg = i == 0;
            bool isLastLeg = i == legs.Count - 1;

            trip.AddTask(TaskType.Move, taskSeq++, leg.FromStationId);

            if (isFirstLeg || legs.Count == 1)
                trip.AddTask(TaskType.Lift, taskSeq++, leg.FromStationId);

            trip.AddTask(TaskType.Move, taskSeq++, leg.ToStationId);

            if (isLastLeg)
                trip.AddTask(TaskType.Drop, taskSeq++, leg.ToStationId);
        }

        trip.Start();

        await _tripRepository.AddAsync(trip, cancellationToken);

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
