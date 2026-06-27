using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using DTMS.Planning.Domain.Services;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.CreateMultiPickDropJob;

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

        int sequence = 1;
        Guid previousStation = Guid.Empty;

        foreach (var pair in request.Pairs)
        {
            var pickupCost = await _costCalc.CalculateCostAsync(previousStation, pair.PickupStationId, cancellationToken);
            job.AddLeg(previousStation, pair.PickupStationId, sequence++, pickupCost);
            previousStation = pair.PickupStationId;

            var dropCost = await _costCalc.CalculateCostAsync(pair.PickupStationId, pair.DropStationId, cancellationToken);
            job.AddLeg(pair.PickupStationId, pair.DropStationId, sequence++, dropCost);
            previousStation = pair.DropStationId;
        }

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
