using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Commands.AssignVehicleToJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Full Auto Planning Pipeline:
///   DeliveryOrder (Validated) → Create Job per Leg → Assign Vehicle → Commit Plan → Trip (auto via PlanCommittedConsumer)
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
        _logger.LogInformation("[AutoPlan] Received Order {OrderId} with {LegCount} leg(s) — starting full auto planning...",
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
                _logger.LogWarning("[AutoPlan] Failed to create Job for Order {OrderId} Leg {Seq}: {Error}",
                    evt.DeliveryOrderId, leg.Sequence, createResult.Error);
                continue;
            }

            var jobId = createResult.Value;
            _logger.LogInformation("[AutoPlan] Step 1/3 ✓ Job {JobId} created for Leg {Seq}", jobId, leg.Sequence);

            // ── Step 2: Assign Vehicle ──
            var assignResult = await _sender.Send(new AssignVehicleToJobCommand(jobId), context.CancellationToken);

            if (!assignResult.IsSuccess)
            {
                _logger.LogWarning("[AutoPlan] Step 2/3 ✗ No vehicle available for Job {JobId}: {Error}. Job remains Created — can be manually assigned later.",
                    jobId, assignResult.Error);
                continue;
            }

            _logger.LogInformation("[AutoPlan] Step 2/3 ✓ Vehicle assigned to Job {JobId}", jobId);

            // ── Step 3: Commit Plan ──
            var commitResult = await _sender.Send(new CommitPlanCommand(jobId), context.CancellationToken);

            if (!commitResult.IsSuccess)
            {
                _logger.LogWarning("[AutoPlan] Step 3/3 ✗ Failed to commit Job {JobId}: {Error}", jobId, commitResult.Error);
                continue;
            }

            _logger.LogInformation("[AutoPlan] Step 3/3 ✓ Job {JobId} committed — Trip will be auto-dispatched via event", jobId);
        }

        _logger.LogInformation("[AutoPlan] ═══ Full pipeline complete for Order {OrderId} ═══", evt.DeliveryOrderId);
    }
}
