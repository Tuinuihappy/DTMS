using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Auto Planning Pipeline:
///   DeliveryOrder (Validated) → Create Job per Leg → Commit Plan → Trip (auto via PlanCommittedConsumer)
///   Vehicle assignment is delegated to RIOT3 at dispatch time.
/// </summary>
public class DeliveryOrderValidatedConsumer : IConsumer<DeliveryOrderReadyForPlanningIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;
    private readonly TenantContext _tenantContext;

    public DeliveryOrderValidatedConsumer(ISender sender, ILogger<DeliveryOrderValidatedConsumer> logger, TenantContext tenantContext)
    {
        _sender = sender;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderReadyForPlanningIntegrationEvent> context)
    {
        var evt = context.Message;
        _tenantContext.Set(evt.TenantId);
        _logger.LogInformation("[AutoPlan] Received Order {OrderId} with {LegCount} leg(s) — starting auto planning...",
            evt.DeliveryOrderId, evt.Legs.Count);

        foreach (var leg in evt.Legs.OrderBy(l => l.Sequence))
        {
            _logger.LogInformation("[AutoPlan] Processing Leg {Seq} ({PickupStationId} → {DropStationId})",
                leg.Sequence, leg.PickupStationId, leg.DropStationId);

            // ── Step 1: Create Job ──
            var createCommand = new CreateJobFromOrderCommand(
                evt.DeliveryOrderId,
                leg.PickupStationId,
                leg.DropStationId,
                evt.Priority);

            var createResult = await _sender.Send(createCommand, context.CancellationToken);

            if (!createResult.IsSuccess)
            {
                _logger.LogWarning("[AutoPlan] Step 1/2 ✗ Failed to create Job for Order {OrderId} Leg {Seq}: {Error}",
                    evt.DeliveryOrderId, leg.Sequence, createResult.Error);
                continue;
            }

            var jobId = createResult.Value;
            _logger.LogInformation("[AutoPlan] Step 1/2 ✓ Job {JobId} created for Leg {Seq}", jobId, leg.Sequence);

            // ── Step 2: Commit Plan (vehicle will be assigned by RIOT3 at dispatch time) ──
            var commitResult = await _sender.Send(new CommitPlanCommand(jobId), context.CancellationToken);

            if (!commitResult.IsSuccess)
            {
                _logger.LogWarning("[AutoPlan] Step 2/2 ✗ Failed to commit Job {JobId}: {Error}", jobId, commitResult.Error);
                continue;
            }

            _logger.LogInformation("[AutoPlan] Step 2/2 ✓ Job {JobId} committed — Trip will be auto-dispatched via event", jobId);
        }

        _logger.LogInformation("[AutoPlan] ═══ Pipeline complete for Order {OrderId} ═══", evt.DeliveryOrderId);
    }
}
