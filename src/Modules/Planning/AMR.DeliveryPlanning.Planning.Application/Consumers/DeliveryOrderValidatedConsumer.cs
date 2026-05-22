using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Application.Commands.AssignPackagesToJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Auto Planning Pipeline:
///   DeliveryOrder (Confirmed) → Group items by station pair → Create Job per group → Commit Plan
/// </summary>
public class DeliveryOrderValidatedConsumer : IConsumer<DeliveryOrderConfirmedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ICarrierTypeProfileRepository _carrierTypeRepo;
    private readonly IShelfRepository _shelfRepo;
    private readonly IShelfManifestRepository _shelfManifestRepo;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;

    public DeliveryOrderValidatedConsumer(
        ISender sender,
        ICarrierTypeProfileRepository carrierTypeRepo,
        IShelfRepository shelfRepo,
        IShelfManifestRepository shelfManifestRepo,
        ILogger<DeliveryOrderValidatedConsumer> logger)
    {
        _sender = sender;
        _carrierTypeRepo = carrierTypeRepo;
        _shelfRepo = shelfRepo;
        _shelfManifestRepo = shelfManifestRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEvent> context)
    {
        var evt = context.Message;

        var stationGroups = evt.Items
            .GroupBy(i => (i.PickupStationId, i.DropStationId))
            .ToList();

        _logger.LogInformation("[AutoPlan] Order {OrderId} with {ItemCount} item(s) in {GroupCount} station group(s) — starting auto planning...",
            evt.DeliveryOrderId, evt.Items.Count, stationGroups.Count);

        foreach (var (groupIndex, stationGroup) in stationGroups.Index())
        {
            var items = stationGroup.ToList();
            var groupWeight = items.Sum(i => i.WeightKg);
            var skus = items.Select(i => i.Sku).ToList();

            _logger.LogInformation("[AutoPlan] Group {G}: {Count} item(s), {Weight}kg ({Pickup} → {Drop})",
                groupIndex + 1, items.Count, groupWeight,
                stationGroup.Key.PickupStationId, stationGroup.Key.DropStationId);

            var createResult = await _sender.Send(new CreateJobFromOrderCommand(
                evt.DeliveryOrderId,
                stationGroup.Key.PickupStationId,
                stationGroup.Key.DropStationId,
                evt.Priority,
                RequiredCapability: null,
                TotalWeight: groupWeight),
                context.CancellationToken);

            if (!createResult.IsSuccess)
            {
                _logger.LogWarning("[AutoPlan] Failed to create job for Group {G}: {Error}", groupIndex + 1, createResult.Error);
                continue;
            }

            var jobId = createResult.Value;

            await AssignPackagesToJobAsync(jobId, skus, context.CancellationToken);

            var commitResult = await _sender.Send(new CommitPlanCommand(jobId), context.CancellationToken);
            if (!commitResult.IsSuccess)
            {
                _logger.LogWarning("[AutoPlan] Failed to commit Job {JobId}: {Error}", jobId, commitResult.Error);
                continue;
            }

            _logger.LogInformation("[AutoPlan] ✓ Job {JobId} committed (Group {G})", jobId, groupIndex + 1);
        }

        _logger.LogInformation("[AutoPlan] ═══ Pipeline complete for Order {OrderId} ═══", evt.DeliveryOrderId);
    }

    private async Task AssignPackagesToJobAsync(Guid jobId, List<string> skus, CancellationToken ct)
    {
        var result = await _sender.Send(new AssignPackagesToJobCommand(jobId, skus), ct);
        if (!result.IsSuccess)
            _logger.LogWarning("[AutoPlan] Failed to assign items to Job {JobId}: {Error}", jobId, result.Error);
    }
}
