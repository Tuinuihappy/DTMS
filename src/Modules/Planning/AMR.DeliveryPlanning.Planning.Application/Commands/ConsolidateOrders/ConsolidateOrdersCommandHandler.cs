using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.ConsolidateOrders;

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

        // Create consolidated Job from multiple orders
        var job = new Job(request.OrderIds, request.Priority, PatternType.Consolidation);

        if (!string.IsNullOrEmpty(request.RequiredCapability))
            job.SetRequiredCapability(request.RequiredCapability);

        // For consolidation: N pickups → 1 common drop
        // Leg 1: Move to common pickup area
        var pickupCost = await _routeCostCalculator.CalculateCostAsync(Guid.Empty, request.PickupStationId, cancellationToken);
        var pickupLeg = job.AddLeg(Guid.Empty, request.PickupStationId, 1, pickupCost);
        pickupLeg.AddStop(request.PickupStationId, StopType.Pickup, 1);

        // Leg 2: Deliver consolidated load to drop station
        var deliveryCost = await _routeCostCalculator.CalculateCostAsync(request.PickupStationId, request.DropStationId, cancellationToken);
        var deliveryLeg = job.AddLeg(request.PickupStationId, request.DropStationId, 2, deliveryCost);
        deliveryLeg.AddStop(request.DropStationId, StopType.Drop, 1);

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
