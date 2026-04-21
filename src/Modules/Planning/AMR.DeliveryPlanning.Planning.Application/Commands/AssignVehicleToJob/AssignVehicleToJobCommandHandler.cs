using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.AssignVehicleToJob;

public class AssignVehicleToJobCommandHandler : ICommandHandler<AssignVehicleToJobCommand>
{
    private readonly IJobRepository _jobRepository;
    private readonly IVehicleSelector _vehicleSelector;

    public AssignVehicleToJobCommandHandler(IJobRepository jobRepository, IVehicleSelector vehicleSelector)
    {
        _jobRepository = jobRepository;
        _vehicleSelector = vehicleSelector;
    }

    public async Task<Result> Handle(AssignVehicleToJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job == null)
            return Result.Failure($"Job {request.JobId} not found.");

        // Get the pickup station from the first leg
        var firstLeg = job.Legs.OrderBy(l => l.SequenceOrder).FirstOrDefault();
        if (firstLeg == null)
            return Result.Failure("Job has no legs defined.");

        var candidate = await _vehicleSelector.SelectBestVehicleAsync(firstLeg.ToStationId, cancellationToken);
        if (candidate == null)
            return Result.Failure("No available vehicle found for this job.");

        // Estimate duration based on total distance / average speed (e.g., 1 m/s)
        var estimatedDuration = job.EstimatedDistance + candidate.DistanceToPickup;

        try
        {
            job.AssignVehicle(candidate.VehicleId, estimatedDuration);
            await _jobRepository.UpdateAsync(job, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
