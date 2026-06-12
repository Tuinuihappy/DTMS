using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobAnchor;

public class CreateJobAnchorCommandHandler : ICommandHandler<CreateJobAnchorCommand, Guid>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<CreateJobAnchorCommandHandler> _logger;

    public CreateJobAnchorCommandHandler(
        IJobRepository jobRepository,
        ILogger<CreateJobAnchorCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(CreateJobAnchorCommand request, CancellationToken cancellationToken)
    {
        var job = new Job(request.DeliveryOrderId, request.Priority);
        job.SetPattern(PatternType.PointToPoint);
        job.SetEnvelopeAnchor(request.GroupIndex, request.PickupStationId, request.DropStationId);
        if (!string.IsNullOrWhiteSpace(request.RequestedTransportMode))
            job.SetTransportMode(request.RequestedTransportMode);
        if (request.SlaDeadline.HasValue)
            job.SetSlaDeadline(request.SlaDeadline.Value);

        // 1 Leg, pickup → drop, cost=0 (envelope dispatch doesn't compute
        // route cost — RIOT3 owns routing). Sequence=1.
        job.AddLeg(request.PickupStationId, request.DropStationId, sequenceOrder: 1, estimatedCost: 0);

        try
        {
            await _jobRepository.AddAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CreateJobAnchor] Failed to persist Job for order {OrderId}", request.DeliveryOrderId);
            return Result<Guid>.Failure($"Job persistence failed: {ex.Message}");
        }

        _logger.LogInformation(
            "[CreateJobAnchor] ✓ Job {JobId} created for order {OrderId} ({Pickup} → {Drop})",
            job.Id, request.DeliveryOrderId, request.PickupStationId, request.DropStationId);

        return Result<Guid>.Success(job.Id);
    }
}
