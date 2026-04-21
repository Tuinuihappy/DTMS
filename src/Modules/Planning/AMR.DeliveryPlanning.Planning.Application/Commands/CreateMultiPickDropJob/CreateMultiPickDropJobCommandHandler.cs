using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateMultiPickDropJob;

public class CreateMultiPickDropJobCommandHandler : ICommandHandler<CreateMultiPickDropJobCommand, Guid>
{
    private readonly IJobRepository _jobRepository;
    private readonly IRouteCostCalculator _costCalc;

    public CreateMultiPickDropJobCommandHandler(IJobRepository jobRepository, IRouteCostCalculator costCalc)
    {
        _jobRepository = jobRepository;
        _costCalc = costCalc;
    }

    public async Task<Result<Guid>> Handle(CreateMultiPickDropJobCommand request, CancellationToken cancellationToken)
    {
        if (request.Pairs.Count == 0)
            return Result<Guid>.Failure("At least one pickup-delivery pair is required.");

        var job = new Job(request.DeliveryOrderId, request.Priority);
        job.SetPattern(PatternType.MultiPickMultiDrop);
        job.SetTotalWeight(request.Pairs.Sum(p => p.Weight));

        if (!string.IsNullOrEmpty(request.RequiredCapability))
            job.SetRequiredCapability(request.RequiredCapability);

        // Sequence legs maintaining precedence: pick(i) → drop(i) for each pair
        int sequence = 1;
        Guid previousStation = Guid.Empty;

        foreach (var pair in request.Pairs)
        {
            // Pickup leg
            var pickupCost = await _costCalc.CalculateCostAsync(previousStation, pair.PickupStationId, cancellationToken);
            var pickupLeg = job.AddLeg(previousStation, pair.PickupStationId, sequence++, pickupCost);
            pickupLeg.AddStop(pair.PickupStationId, StopType.Pickup, 1);
            previousStation = pair.PickupStationId;

            // Drop leg
            var dropCost = await _costCalc.CalculateCostAsync(pair.PickupStationId, pair.DropStationId, cancellationToken);
            var dropLeg = job.AddLeg(pair.PickupStationId, pair.DropStationId, sequence++, dropCost);
            dropLeg.AddStop(pair.DropStationId, StopType.Drop, 1);
            previousStation = pair.DropStationId;
        }

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
