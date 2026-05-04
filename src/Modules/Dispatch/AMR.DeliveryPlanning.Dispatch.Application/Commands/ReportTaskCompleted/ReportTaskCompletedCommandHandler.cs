using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskCompleted;

public class ReportTaskCompletedCommandHandler : ICommandHandler<ReportTaskCompletedCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly ITaskDispatcher _taskDispatcher;
    private readonly ILogger<ReportTaskCompletedCommandHandler> _logger;

    public ReportTaskCompletedCommandHandler(
        ITripRepository tripRepository,
        ITaskDispatcher taskDispatcher,
        ILogger<ReportTaskCompletedCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _taskDispatcher = taskDispatcher;
        _logger = logger;
    }

    public async Task<Result> Handle(ReportTaskCompletedCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null)
            return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            trip.CompleteTask(request.TaskId);

            await _tripRepository.UpdateAsync(trip, cancellationToken);

            if (trip.Status == TripStatus.Completed)
            {
                return Result.Success();
            }
            else if (trip.Status == TripStatus.InProgress)
            {
                var nextTask = trip.Tasks
                    .OrderBy(t => t.SequenceOrder)
                    .FirstOrDefault(t => t.Status == Domain.Enums.TaskStatus.Dispatched);

                if (nextTask != null)
                {
                    try
                    {
                        await _taskDispatcher.DispatchAsync(trip.VehicleId, nextTask, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send next task {TaskId} to vendor for trip {TripId}", nextTask.Id, trip.Id);
                    }
                }
            }

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
