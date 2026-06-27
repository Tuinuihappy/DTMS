using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Domain.Services;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.ConsolidateOrders;

public class ConsolidateOrdersCommandHandler : ICommandHandler<ConsolidateOrdersCommand, Guid>
{
    private readonly IJobRepository _jobRepository;
    private readonly IRouteCostCalculator _routeCostCalculator;

    public ConsolidateOrdersCommandHandler(IJobRepository jobRepository, IRouteCostCalculator routeCostCalculator)
    {
        _jobRepository = jobRepository;
        _routeCostCalculator = routeCostCalculator;
    }

    public async Task<Result<Guid>> Handle(ConsolidateOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.OrderIds.Count < 2)
            return Result<Guid>.Failure("Consolidation requires at least 2 orders.");

        var job = new Job(request.OrderIds, request.Priority, PatternType.Consolidation);

        if (!string.IsNullOrEmpty(request.RequiredCapability))
            job.SetRequiredCapability(request.RequiredCapability);

        var pickupCost = await _routeCostCalculator.CalculateCostAsync(Guid.Empty, request.PickupStationId, cancellationToken);
        job.AddLeg(Guid.Empty, request.PickupStationId, 1, pickupCost);

        var deliveryCost = await _routeCostCalculator.CalculateCostAsync(request.PickupStationId, request.DropStationId, cancellationToken);
        job.AddLeg(request.PickupStationId, request.DropStationId, 2, deliveryCost);

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
