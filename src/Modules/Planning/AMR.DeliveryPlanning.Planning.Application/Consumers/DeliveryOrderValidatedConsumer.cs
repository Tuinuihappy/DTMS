using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Application.Commands.AssignPackagesToJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Consumers;

/// <summary>
/// Auto Planning Pipeline:
///   DeliveryOrder (ReadyToPlan) → Split packages by carrier capacity → Create Job per group → Commit Plan
///   Shelf jobs: auto-assign available shelf → create ShelfManifest
/// </summary>
public class DeliveryOrderValidatedConsumer : IConsumer<DeliveryOrderReadyForPlanningIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ICarrierTypeProfileRepository _carrierTypeRepo;
    private readonly IShelfRepository _shelfRepo;
    private readonly IShelfManifestRepository _shelfManifestRepo;
    private readonly ILogger<DeliveryOrderValidatedConsumer> _logger;
    private readonly TenantContext _tenantContext;

    public DeliveryOrderValidatedConsumer(
        ISender sender,
        ICarrierTypeProfileRepository carrierTypeRepo,
        IShelfRepository shelfRepo,
        IShelfManifestRepository shelfManifestRepo,
        ILogger<DeliveryOrderValidatedConsumer> logger,
        TenantContext tenantContext)
    {
        _sender = sender;
        _carrierTypeRepo = carrierTypeRepo;
        _shelfRepo = shelfRepo;
        _shelfManifestRepo = shelfManifestRepo;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task Consume(ConsumeContext<DeliveryOrderReadyForPlanningIntegrationEvent> context)
    {
        var evt = context.Message;
        _tenantContext.Set(evt.TenantId);
        _logger.LogInformation("[AutoPlan] Order {OrderId} with {LegCount} leg(s) — starting auto planning...",
            evt.DeliveryOrderId, evt.Legs.Count);

        foreach (var leg in evt.Legs.OrderBy(l => l.Sequence))
        {
            var carrierProfile = await _carrierTypeRepo.GetByCodeAsync(leg.CarrierTypeCode, context.CancellationToken);
            if (carrierProfile is null)
            {
                _logger.LogWarning("[AutoPlan] Unknown CarrierTypeCode '{Code}' for Leg {Seq} — skipping.", leg.CarrierTypeCode, leg.Sequence);
                continue;
            }

            // Split packages into groups respecting MaxSlots and MaxWeightKg
            var groups = SplitPackages(leg.Packages, carrierProfile.MaxSlots, carrierProfile.MaxWeightKg);
            _logger.LogInformation("[AutoPlan] Leg {Seq} [{CarrierType}]: {Count} packages → {Groups} job(s)",
                leg.Sequence, leg.CarrierTypeCode, leg.Packages.Count, groups.Count);

            foreach (var (groupIndex, group) in groups.Index())
            {
                var groupWeight = group.Sum(p => p.GrossWeightKg);
                var barcodes = group.Select(p => p.Barcode).ToList();

                _logger.LogInformation("[AutoPlan] Leg {Seq} Group {G}: {Count} packages, {Weight}kg [{Capability}]",
                    leg.Sequence, groupIndex + 1, group.Count, groupWeight, carrierProfile.AMRCapability);

                // ── Step 1: Create Job ──
                var createResult = await _sender.Send(new CreateJobFromOrderCommand(
                    evt.DeliveryOrderId,
                    leg.PickupStationId,
                    leg.DropStationId,
                    evt.SlaTier,
                    RequiredCapability: carrierProfile.AMRCapability,
                    TotalWeight: groupWeight),
                    context.CancellationToken);

                if (!createResult.IsSuccess)
                {
                    _logger.LogWarning("[AutoPlan] Failed to create job for Leg {Seq} Group {G}: {Error}",
                        leg.Sequence, groupIndex + 1, createResult.Error);
                    continue;
                }

                var jobId = createResult.Value;

                // ── Step 2: Assign packages to job ──
                // (stored in Job.PackageBarcodes via SetPackageBarcodes — done in CreateJobFromOrderCommand scope)
                // Note: we pass barcodes through additional command or job update
                await AssignPackagesToJobAsync(jobId, barcodes, context.CancellationToken);

                // ── Step 3: For Shelf carrier — auto-assign an available shelf ──
                if (leg.CarrierTypeCode == "SHELF")
                    await AssignShelfAsync(jobId, barcodes, groupWeight, carrierProfile.MaxWeightKg, context.CancellationToken);

                // ── Step 4: Commit Plan ──
                var commitResult = await _sender.Send(new CommitPlanCommand(jobId), context.CancellationToken);
                if (!commitResult.IsSuccess)
                {
                    _logger.LogWarning("[AutoPlan] Failed to commit Job {JobId}: {Error}", jobId, commitResult.Error);
                    continue;
                }

                _logger.LogInformation("[AutoPlan] ✓ Job {JobId} committed (Leg {Seq} Group {G})", jobId, leg.Sequence, groupIndex + 1);
            }
        }

        _logger.LogInformation("[AutoPlan] ═══ Pipeline complete for Order {OrderId} ═══", evt.DeliveryOrderId);
    }

    // ── Package splitting algorithm ────────────────────────────────────────────

    private static List<List<PackageSummaryDto>> SplitPackages(
        IReadOnlyList<PackageSummaryDto> packages,
        int? maxSlots,
        double? maxWeightKg)
    {
        var groups = new List<List<PackageSummaryDto>>();
        var current = new List<PackageSummaryDto>();
        double currentWeight = 0;

        foreach (var pkg in packages)
        {
            var slotsExceeded = maxSlots.HasValue && current.Count >= maxSlots.Value;
            var weightExceeded = maxWeightKg.HasValue && currentWeight + pkg.GrossWeightKg > maxWeightKg.Value;

            if ((slotsExceeded || weightExceeded) && current.Count > 0)
            {
                groups.Add(current);
                current = new List<PackageSummaryDto>();
                currentWeight = 0;
            }

            current.Add(pkg);
            currentWeight += pkg.GrossWeightKg;
        }

        if (current.Count > 0)
            groups.Add(current);

        return groups.Count > 0 ? groups : [[]];
    }

    // ── Package assignment (update job via mediator) ───────────────────────────

    private async Task AssignPackagesToJobAsync(Guid jobId, List<string> barcodes, CancellationToken ct)
    {
        var result = await _sender.Send(new AssignPackagesToJobCommand(jobId, barcodes), ct);
        if (!result.IsSuccess)
            _logger.LogWarning("[AutoPlan] Failed to assign packages to Job {JobId}: {Error}", jobId, result.Error);
    }

    // ── Shelf auto-assignment ──────────────────────────────────────────────────

    private async Task AssignShelfAsync(Guid jobId, List<string> barcodes, double groupWeight, double? maxWeight, CancellationToken ct)
    {
        var availableShelves = await _shelfRepo.GetAllAvailableAsync(groupWeight, barcodes.Count, ct);
        var shelf = availableShelves.FirstOrDefault();

        if (shelf is null)
        {
            _logger.LogWarning("[AutoPlan] No available shelf found for Job {JobId} (weight={Weight}kg, count={Count})",
                jobId, groupWeight, barcodes.Count);
            return;
        }

        shelf.SetInUse();
        await _shelfRepo.UpdateAsync(shelf, ct);

        var manifest = new ShelfManifest(jobId, shelf.Rfid, barcodes);
        await _shelfManifestRepo.AddAsync(manifest, ct);
        await _shelfManifestRepo.SaveChangesAsync(ct);

        _logger.LogInformation("[AutoPlan] Shelf {Rfid} assigned to Job {JobId} with {Count} packages",
            shelf.Rfid, jobId, barcodes.Count);
    }
}
