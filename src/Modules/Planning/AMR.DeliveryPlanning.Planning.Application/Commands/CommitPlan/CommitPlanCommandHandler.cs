using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;

public class CommitPlanCommandHandler : ICommandHandler<CommitPlanCommand>
{
    private readonly IJobRepository _jobRepository;
    private readonly IEventBus _eventBus;

    public CommitPlanCommandHandler(IJobRepository jobRepository, IEventBus eventBus)
    {
        _jobRepository = jobRepository;
        _eventBus = eventBus;
    }

    public async Task<Result> Handle(CommitPlanCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job == null)
            return Result.Failure($"Job {request.JobId} not found.");

        try
        {
            job.Commit();
            await _jobRepository.UpdateAsync(job, cancellationToken);

            var legs = job.Legs
                .OrderBy(l => l.SequenceOrder)
                .Select(l => new PlannedLegDto(l.FromStationId, l.ToStationId, l.SequenceOrder))
                .ToList();

            // Publish integration event → Dispatch module auto-creates a Trip
            await _eventBus.PublishAsync(new PlanCommittedIntegrationEvent(
                Guid.NewGuid(),
                DateTime.UtcNow,
                job.Id,
                job.AssignedVehicleId ?? Guid.Empty,
                legs), cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}

