using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;

public class CommitPlanCommandHandler : ICommandHandler<CommitPlanCommand>
{
    private readonly IJobRepository _jobRepository;
    private readonly IEventBus _eventBus;
    private readonly ITenantContext _tenantContext;

    public CommitPlanCommandHandler(IJobRepository jobRepository, IEventBus eventBus, ITenantContext tenantContext)
    {
        _jobRepository = jobRepository;
        _eventBus = eventBus;
        _tenantContext = tenantContext;
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
                _tenantContext.TenantId,
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

