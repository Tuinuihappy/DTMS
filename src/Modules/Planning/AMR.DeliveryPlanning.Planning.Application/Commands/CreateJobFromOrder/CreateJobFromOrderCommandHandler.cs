using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;

public class CreateJobFromOrderCommandHandler : ICommandHandler<CreateJobFromOrderCommand, Guid>
{
    private readonly IJobRepository _jobRepository;
    private readonly IRouteCostCalculator _routeCostCalculator;
    private readonly IRouteSolver _routeSolver;
    private readonly ITenantContext _tenantContext;

    public CreateJobFromOrderCommandHandler(
        IJobRepository jobRepository,
        IRouteCostCalculator routeCostCalculator,
        IRouteSolver routeSolver,
        ITenantContext tenantContext)
    {
        _jobRepository = jobRepository;
        _routeCostCalculator = routeCostCalculator;
        _routeSolver = routeSolver;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(CreateJobFromOrderCommand request, CancellationToken cancellationToken)
    {
        // Collect all drop stations
        var allDrops = new List<Guid> { request.DropStationId };
        if (request.AdditionalDropStationIds?.Count > 0)
            allDrops.AddRange(request.AdditionalDropStationIds);

        // Classify pattern
        var pattern = allDrops.Count > 1 ? PatternType.MultiStop : PatternType.PointToPoint;

        var job = new Job(_tenantContext.TenantId, request.DeliveryOrderId, request.Priority);
        job.SetPattern(pattern);

        if (!string.IsNullOrEmpty(request.RequiredCapability))
            job.SetRequiredCapability(request.RequiredCapability);
        if (request.TotalWeight > 0)
            job.SetTotalWeight(request.TotalWeight);

        // Leg 1: Move to pickup station
        var pickupCost = await _routeCostCalculator.CalculateCostAsync(Guid.Empty, request.PickupStationId, cancellationToken);
        var pickupLeg = job.AddLeg(Guid.Empty, request.PickupStationId, 1, pickupCost);
        pickupLeg.AddStop(request.PickupStationId, StopType.Pickup, 1);

        if (pattern == PatternType.MultiStop)
        {
            // Solve TSP for optimal drop sequence
            var optimizedRoute = _routeSolver.SolveRoute(request.PickupStationId, allDrops);

            var previousStation = request.PickupStationId;
            for (int i = 0; i < optimizedRoute.Count; i++)
            {
                var dropStation = optimizedRoute[i];
                var cost = await _routeCostCalculator.CalculateCostAsync(previousStation, dropStation, cancellationToken);
                var leg = job.AddLeg(previousStation, dropStation, i + 2, cost);
                leg.AddStop(dropStation, StopType.Drop, 1);
                previousStation = dropStation;
            }
        }
        else
        {
            // Simple point-to-point: 1 delivery leg
            var deliveryCost = await _routeCostCalculator.CalculateCostAsync(request.PickupStationId, request.DropStationId, cancellationToken);
            var deliveryLeg = job.AddLeg(request.PickupStationId, request.DropStationId, 2, deliveryCost);
            deliveryLeg.AddStop(request.DropStationId, StopType.Drop, 1);
        }

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
