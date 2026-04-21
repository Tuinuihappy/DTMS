using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.ReplanJob;

public class ReplanJobCommandHandler : ICommandHandler<ReplanJobCommand>
{
    private readonly IJobRepository _jobRepository;
    private readonly IVehicleSelector _vehicleSelector;
    private readonly ILogger<ReplanJobCommandHandler> _logger;

    public ReplanJobCommandHandler(IJobRepository jobRepository, IVehicleSelector vehicleSelector, ILogger<ReplanJobCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _vehicleSelector = vehicleSelector;
        _logger = logger;
    }

    public async Task<Result> Handle(ReplanJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdAsync(request.JobId, cancellationToken);
        if (job == null)
            return Result.Failure($"Job {request.JobId} not found.");

        try
        {
            // Reset job to Created status
            job.Replan(request.Reason);
            _logger.LogInformation("Job {JobId} replanned. Reason: {Reason}", request.JobId, request.Reason);

            // Re-assign vehicle
            var pickupStationId = job.Legs.FirstOrDefault()?.ToStationId ?? Guid.Empty;
            var candidate = await _vehicleSelector.SelectBestVehicleAsync(pickupStationId, cancellationToken);

            if (candidate != null)
            {
                job.AssignVehicle(candidate.VehicleId, candidate.DistanceToPickup * 2);
                _logger.LogInformation("Job {JobId} re-assigned to Vehicle {VehicleId}", request.JobId, candidate.VehicleId);
            }
            else
            {
                _logger.LogWarning("No vehicle available for replanned Job {JobId}", request.JobId);
            }

            await _jobRepository.UpdateAsync(job, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
