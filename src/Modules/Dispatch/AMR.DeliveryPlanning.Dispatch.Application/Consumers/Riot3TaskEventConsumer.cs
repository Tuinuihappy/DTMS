using AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskCompleted;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskFailed;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Consumers;

public class Riot3TaskCompletedConsumer : IConsumer<Riot3TaskCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ITripRepository _tripRepo;
    private readonly ILogger<Riot3TaskCompletedConsumer> _logger;

    public Riot3TaskCompletedConsumer(ISender sender, ITripRepository tripRepo, ILogger<Riot3TaskCompletedConsumer> logger)
    {
        _sender = sender;
        _tripRepo = tripRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Riot3TaskCompletedIntegrationEvent> context)
    {
        var taskId = context.Message.TaskId;
        _logger.LogInformation("RIOT3 task completed callback: {TaskId}", taskId);

        // Find the trip that owns this task
        var trip = await FindTripByTaskId(taskId, context.CancellationToken);
        if (trip == null)
        {
            _logger.LogWarning("No trip found for completed task {TaskId}", taskId);
            return;
        }

        await _sender.Send(new ReportTaskCompletedCommand(trip.Id, taskId), context.CancellationToken);
    }

    private async Task<Domain.Entities.Trip?> FindTripByTaskId(Guid taskId, CancellationToken ct)
    {
        // In production this would be a dedicated query; simplified here
        var activeTasks = await _tripRepo.GetActiveTripsByVehicleAsync(Guid.Empty, ct);
        return activeTasks.FirstOrDefault(t => t.Tasks.Any(task => task.Id == taskId));
    }
}

public class Riot3TaskFailedConsumer : IConsumer<Riot3TaskFailedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ITripRepository _tripRepo;
    private readonly ILogger<Riot3TaskFailedConsumer> _logger;

    public Riot3TaskFailedConsumer(ISender sender, ITripRepository tripRepo, ILogger<Riot3TaskFailedConsumer> logger)
    {
        _sender = sender;
        _tripRepo = tripRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Riot3TaskFailedIntegrationEvent> context)
    {
        var taskId = context.Message.TaskId;
        _logger.LogWarning("RIOT3 task failed callback: {TaskId} [{Code}] {Msg}",
            taskId, context.Message.ErrorCode, context.Message.ErrorMessage);

        var trip = await FindTripByTaskId(taskId, context.CancellationToken);
        if (trip == null)
        {
            _logger.LogWarning("No trip found for failed task {TaskId}", taskId);
            return;
        }

        // Report task failed + raise exception for operator visibility
        await _sender.Send(new ReportTaskFailedCommand(trip.Id, taskId, context.Message.ErrorMessage), context.CancellationToken);
        await _sender.Send(new RaiseExceptionCommand(
            trip.Id, context.Message.ErrorCode, "HIGH", context.Message.ErrorMessage), context.CancellationToken);
    }

    private async Task<Domain.Entities.Trip?> FindTripByTaskId(Guid taskId, CancellationToken ct)
    {
        var activeTasks = await _tripRepo.GetActiveTripsByVehicleAsync(Guid.Empty, ct);
        return activeTasks.FirstOrDefault(t => t.Tasks.Any(task => task.Id == taskId));
    }
}
