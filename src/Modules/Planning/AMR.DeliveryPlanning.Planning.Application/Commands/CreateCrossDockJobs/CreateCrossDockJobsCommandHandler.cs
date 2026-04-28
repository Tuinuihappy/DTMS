using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateCrossDockJobs;

public class CreateCrossDockJobsCommandHandler : ICommandHandler<CreateCrossDockJobsCommand, CrossDockResult>
{
    private readonly IJobRepository _jobRepository;
    private readonly IRouteCostCalculator _costCalc;
    private readonly ITenantContext _tenantContext;

    public CreateCrossDockJobsCommandHandler(IJobRepository jobRepository, IRouteCostCalculator costCalc, ITenantContext tenantContext)
    {
        _jobRepository = jobRepository;
        _costCalc = costCalc;
        _tenantContext = tenantContext;
    }

    public async Task<Result<CrossDockResult>> Handle(CreateCrossDockJobsCommand request, CancellationToken cancellationToken)
    {
        // ── Inbound Job: pickup → dock ──
        var inboundJob = new Job(_tenantContext.TenantId, request.InboundOrderId, request.Priority);
        inboundJob.SetPattern(PatternType.CrossDock);

        var pickupCost = await _costCalc.CalculateCostAsync(Guid.Empty, request.InboundPickupStationId, cancellationToken);
        var pickupLeg = inboundJob.AddLeg(Guid.Empty, request.InboundPickupStationId, 1, pickupCost);
        pickupLeg.AddStop(request.InboundPickupStationId, StopType.Pickup, 1);

        var toDockCost = await _costCalc.CalculateCostAsync(request.InboundPickupStationId, request.DockStationId, cancellationToken);
        var dockLeg = inboundJob.AddLeg(request.InboundPickupStationId, request.DockStationId, 2, toDockCost);
        dockLeg.AddStop(request.DockStationId, StopType.Drop, 1);

        await _jobRepository.AddAsync(inboundJob, cancellationToken);

        // ── Outbound Job: dock → drop ──
        var outboundJob = new Job(_tenantContext.TenantId, request.OutboundOrderId, request.Priority);
        outboundJob.SetPattern(PatternType.CrossDock);

        var fromDockCost = await _costCalc.CalculateCostAsync(Guid.Empty, request.DockStationId, cancellationToken);
        var dockPickupLeg = outboundJob.AddLeg(Guid.Empty, request.DockStationId, 1, fromDockCost);
        dockPickupLeg.AddStop(request.DockStationId, StopType.Pickup, 1);

        var deliveryCost = await _costCalc.CalculateCostAsync(request.DockStationId, request.OutboundDropStationId, cancellationToken);
        var deliveryLeg = outboundJob.AddLeg(request.DockStationId, request.OutboundDropStationId, 2, deliveryCost);
        deliveryLeg.AddStop(request.OutboundDropStationId, StopType.Drop, 1);

        await _jobRepository.AddAsync(outboundJob, cancellationToken);

        // ── Dependency: inbound must complete before outbound starts ──
        var dependency = new JobDependency(
            inboundJob.Id,
            outboundJob.Id,
            "CROSS_DOCK",
            TimeSpan.FromMinutes(request.HandlingDwellMinutes));

        await _jobRepository.AddDependencyAsync(dependency, cancellationToken);

        return Result<CrossDockResult>.Success(
            new CrossDockResult(inboundJob.Id, outboundJob.Id, dependency.Id));
    }
}
