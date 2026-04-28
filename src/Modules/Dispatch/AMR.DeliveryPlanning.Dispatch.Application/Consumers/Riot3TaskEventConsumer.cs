using AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskCompleted;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskFailed;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Consumers;

public class Riot3TaskCompletedConsumer : IConsumer<Riot3TaskCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ITripRepository _tripRepo;
    private readonly ILogger<Riot3TaskCompletedConsumer> _logger;
    private readonly TenantContext _tenantContext;

    public Riot3TaskCompletedConsumer(ISender sender, ITripRepository tripRepo,
        ILogger<Riot3TaskCompletedConsumer> logger, TenantContext tenantContext)
    {
        _sender = sender;
        _tripRepo = tripRepo;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task Consume(ConsumeContext<Riot3TaskCompletedIntegrationEvent> context)
    {
        var taskId = context.Message.TaskId;
        _logger.LogInformation("RIOT3 task completed callback: {TaskId}", taskId);

        // GetTripByTaskIdAsync uses IgnoreQueryFilters to find the trip cross-tenant;
        // once found, we establish the tenant context so subsequent commands operate
        // within the correct tenant boundary.
        var trip = await _tripRepo.GetTripByTaskIdAsync(taskId, context.CancellationToken);
        if (trip == null)
        {
            _logger.LogWarning("No trip found for completed task {TaskId}", taskId);
            return;
        }

        _tenantContext.Set(trip.TenantId);
        await _sender.Send(new ReportTaskCompletedCommand(trip.Id, taskId), context.CancellationToken);
    }
}

public class Riot3TaskFailedConsumer : IConsumer<Riot3TaskFailedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ITripRepository _tripRepo;
    private readonly ILogger<Riot3TaskFailedConsumer> _logger;
    private readonly TenantContext _tenantContext;

    public Riot3TaskFailedConsumer(ISender sender, ITripRepository tripRepo,
        ILogger<Riot3TaskFailedConsumer> logger, TenantContext tenantContext)
    {
        _sender = sender;
        _tripRepo = tripRepo;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task Consume(ConsumeContext<Riot3TaskFailedIntegrationEvent> context)
    {
        var taskId = context.Message.TaskId;
        _logger.LogWarning("RIOT3 task failed callback: {TaskId} [{Code}] {Msg}",
            taskId, context.Message.ErrorCode, context.Message.ErrorMessage);

        var trip = await _tripRepo.GetTripByTaskIdAsync(taskId, context.CancellationToken);
        if (trip == null)
        {
            _logger.LogWarning("No trip found for failed task {TaskId}", taskId);
            return;
        }

        _tenantContext.Set(trip.TenantId);
        await _sender.Send(new ReportTaskFailedCommand(trip.Id, taskId, context.Message.ErrorMessage), context.CancellationToken);
        await _sender.Send(new RaiseExceptionCommand(
            trip.Id, context.Message.ErrorCode, "HIGH", context.Message.ErrorMessage), context.CancellationToken);
    }
}
