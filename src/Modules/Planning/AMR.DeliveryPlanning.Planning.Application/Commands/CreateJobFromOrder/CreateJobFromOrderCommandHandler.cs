using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;

public class CreateJobFromOrderCommandHandler : ICommandHandler<CreateJobFromOrderCommand, Guid>
{
    private readonly IJobRepository _jobRepository;
    private readonly IRouteCostCalculator _routeCostCalculator;

    public CreateJobFromOrderCommandHandler(IJobRepository jobRepository, IRouteCostCalculator routeCostCalculator)
    {
        _jobRepository = jobRepository;
        _routeCostCalculator = routeCostCalculator;
    }

    public async Task<Result<Guid>> Handle(CreateJobFromOrderCommand request, CancellationToken cancellationToken)
    {
        var job = new Job(request.DeliveryOrderId, request.Priority);

        // Leg 1: Pickup leg (current position → pickup station)
        var pickupCost = await _routeCostCalculator.CalculateCostAsync(Guid.Empty, request.PickupStationId, cancellationToken);
        var pickupLeg = job.AddLeg(Guid.Empty, request.PickupStationId, 1, pickupCost);
        pickupLeg.AddStop(request.PickupStationId, StopType.Pickup, 1);

        // Leg 2: Delivery leg (pickup station → drop station)
        var deliveryCost = await _routeCostCalculator.CalculateCostAsync(request.PickupStationId, request.DropStationId, cancellationToken);
        var deliveryLeg = job.AddLeg(request.PickupStationId, request.DropStationId, 2, deliveryCost);
        deliveryLeg.AddStop(request.DropStationId, StopType.Drop, 1);

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
