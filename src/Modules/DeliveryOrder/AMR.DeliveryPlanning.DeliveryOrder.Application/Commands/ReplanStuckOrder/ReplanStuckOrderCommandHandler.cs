using AMR.DeliveryPlanning.DeliveryOrder.Application.Options;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReplanStuckOrder;

/// <summary>
/// T1.7 handler. Builds and publishes
/// <see cref="DeliveryOrderConfirmedIntegrationEventV1"/> directly via the
/// MassTransit bus rather than through the domain (which would require
/// Status=Confirmed). Pre-validates that the order is in a replan-safe state
/// and has no active Trip, then publishes — the Planning consumer takes it
/// from there. Audit-logged so an operator-initiated replay is distinguishable
/// from a watchdog-initiated one in <c>OrderAuditEvents</c>.
/// </summary>
public class ReplanStuckOrderCommandHandler : ICommandHandler<ReplanStuckOrderCommand, ReplanStuckOrderResult>
{
    private static readonly HashSet<OrderStatus> ReplayableStatuses = new()
    {
        OrderStatus.Confirmed, OrderStatus.Planning, OrderStatus.Planned, OrderStatus.Dispatched
    };

    private readonly IDeliveryOrderRepository _repository;
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly ITripRepository _tripRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IPublishEndpoint _publisher;
    private readonly DeliveryOrderOptions _options;
    private readonly ILogger<ReplanStuckOrderCommandHandler> _logger;

    public ReplanStuckOrderCommandHandler(
        IDeliveryOrderRepository repository,
        IOrderAuditEventRepository auditRepo,
        ITripRepository tripRepository,
        IJobRepository jobRepository,
        IPublishEndpoint publisher,
        IOptions<DeliveryOrderOptions> options,
        ILogger<ReplanStuckOrderCommandHandler> logger)
    {
        _repository = repository;
        _auditRepo = auditRepo;
        _tripRepository = tripRepository;
        _jobRepository = jobRepository;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ReplanStuckOrderResult>> Handle(ReplanStuckOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TriggeredBy))
            return Result<ReplanStuckOrderResult>.Failure("TriggeredBy is required.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<ReplanStuckOrderResult>.Failure("Reason is required.");

        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
            return Result<ReplanStuckOrderResult>.Failure($"Order {request.OrderId} not found.");

        if (!ReplayableStatuses.Contains(order.Status))
            return Result<ReplanStuckOrderResult>.Failure(
                $"Cannot replan an order in {order.Status} status — only Confirmed/Planning/Planned/Dispatched are replayable. " +
                "Use /reopen for terminal-state orders.");

        // RequireStuckPlanned narrows the safety check for the watchdog — it
        // calls with RequireStuckPlanned=true so an order that moves on its
        // own between scan and execute is skipped, not double-replayed.
        if (request.RequireStuckPlanned && order.Status != OrderStatus.Planned)
            return Result<ReplanStuckOrderResult>.Failure(
                $"Order {order.Id} is at {order.Status}, no longer stuck-Planned — skipping replay.");

        // Block replay if any Trip is in flight — same guard as /redispatch.
        // The Planning consumer is idempotent for group-level dispatch but
        // not for items already bound to a vendor trip; an active Trip means
        // the workflow is making progress and replay would double-dispatch.
        var trips = await _tripRepository.GetByDeliveryOrderIdAsync(order.Id, cancellationToken);
        var hasActiveTrip = trips.Any(t =>
            t.Status is Dispatch.Domain.Enums.TripStatus.Created
                or Dispatch.Domain.Enums.TripStatus.InProgress
                or Dispatch.Domain.Enums.TripStatus.Paused);
        if (hasActiveTrip)
            return Result<ReplanStuckOrderResult>.Failure(
                "Cannot replan — at least one trip on this order is still active. " +
                "Use /trips/{id}/retry on the specific trip instead.");

        // T1.8 — vendor-acceptance guard. The OD-0381 incident showed that the
        // active-Trip check alone misses a case: vendor (RIOT3) accepted the
        // upperKey on a prior attempt, then Trip persistence or MarkJobDispatched
        // failed before that Trip became visible — leaving Job.VendorOrderKey
        // set but no Trip row. Replaying then sends the same upperKey, RIOT3
        // rejects with E110007 "upper-level unique key duplicate", and the
        // order spins in a loop forever. If any Job for this order already has
        // a VendorOrderKey, vendor reconciliation is the right tool — not
        // another dispatch attempt.
        var jobs = await _jobRepository.GetByDeliveryOrderIdAsync(order.Id, cancellationToken);
        var vendorAcceptedJob = jobs.FirstOrDefault(j => !string.IsNullOrEmpty(j.VendorOrderKey));
        if (vendorAcceptedJob is not null)
            return Result<ReplanStuckOrderResult>.Failure(
                $"Cannot replan — vendor already accepted upperKey for job {vendorAcceptedJob.Id} " +
                $"(VendorOrderKey={vendorAcceptedJob.VendorOrderKey}). " +
                "A replay would attempt a duplicate dispatch. Use vendor reconciliation " +
                "or /trips/{tripId}/retry on a specific trip instead.");

        // Items must have PickupStationId/DropStationId resolved — they are
        // set during MarkAsValidated. A missing station ID means the order
        // shouldn't have advanced past Validated and replaying won't help.
        var unmapped = order.Items.Where(i => i.PickupStationId is null || i.DropStationId is null).ToList();
        if (unmapped.Count > 0)
            return Result<ReplanStuckOrderResult>.Failure(
                $"{unmapped.Count} item(s) lack resolved station IDs — order cannot be replanned without re-validation.");

        var items = order.Items.Select(i => new ItemSummaryDto(
            i.ItemId,
            i.WeightKg ?? _options.WeightFallbackKg,
            i.PickupStationId!.Value,
            i.DropStationId!.Value,
            i.Hazmat is { } hz ? new ItemHazmatSummaryDto(hz.ClassCode, hz.PackingGroup?.ToString()) : null,
            i.Temperature is { } tr ? new ItemTemperatureSummaryDto(tr.MinC, tr.MaxC) : null,
            i.HandlingInstructions.Count > 0
                ? i.HandlingInstructions.Select(h => h.ToString()).ToList()
                : null)).ToList();

        var publishedAt = DateTime.UtcNow;
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            EventId: Guid.NewGuid(),
            OccurredOn: publishedAt,
            DeliveryOrderId: order.Id,
            Priority: order.Priority.ToString(),
            EarliestUtc: order.ServiceWindow?.EarliestUtc,
            LatestUtc: order.ServiceWindow?.LatestUtc,
            SubmittedAt: order.SubmittedAt,
            Items: items,
            RequestedTransportMode: order.RequestedTransportMode?.ToString());

        var previousStatus = order.Status.ToString();
        await _publisher.Publish(evt, cancellationToken);

        await _auditRepo.AddAsync(new OrderAuditEvent(
            order.Id, "OrderReplanned",
            $"Order '{order.OrderRef}' replanned from {previousStatus} by {request.TriggeredBy}: {request.Reason}"),
            cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "[Replan] Order {OrderId} '{OrderRef}' (was {Status}) replanned by {By}: {Reason}",
            order.Id, order.OrderRef, previousStatus, request.TriggeredBy, request.Reason);

        return Result<ReplanStuckOrderResult>.Success(
            new ReplanStuckOrderResult(order.Id, previousStatus, items.Count, publishedAt));
    }
}
