using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P3 — Hour-bucketed status counters for the operator dashboard.
/// Every Order lifecycle integration event increments the matching status
/// column for its OccurredOn-aligned hour bucket. Single projection
/// powers both KpiRail's "today" totals and the DispatchFunnel chart
/// (UI sums or reads columns as needed).
///
/// Idempotent + safe under at-least-once delivery via the standard inbox
/// pattern.
/// </summary>
public class OrderFunnelProjector :
    IConsumer<DeliveryOrderConfirmedIntegrationEventV1>,
    IConsumer<DeliveryOrderDispatchedIntegrationEventV1>,
    IConsumer<DeliveryOrderInProgressIntegrationEventV1>,
    IConsumer<DeliveryOrderCompletedIntegrationEventV1>,
    IConsumer<DeliveryOrderPartiallyCompletedIntegrationEventV1>,
    IConsumer<DeliveryOrderFailedIntegrationEventV1>,
    IConsumer<DeliveryOrderCancelledIntegrationEventV1>,
    IConsumer<DeliveryOrderRejectedIntegrationEventV1>,
    IConsumer<DeliveryOrderHeldIntegrationEventV1>,
    IConsumer<DeliveryOrderReleasedIntegrationEventV1>
{
    public const string Name = nameof(OrderFunnelProjector);

    private readonly IOrderFunnelProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<OrderFunnelProjector> _logger;

    public OrderFunnelProjector(
        IOrderFunnelProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<OrderFunnelProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Project(ctx, "Confirmed");

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Project(ctx, "Dispatched");

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Project(ctx, "InProgress");

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Project(ctx, "Completed");

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Project(ctx, "PartiallyCompleted");

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Project(ctx, "Failed");

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Project(ctx, "Cancelled");

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Project(ctx, "Rejected");

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Project(ctx, "Held");

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        => Project(ctx, "Released");

    private async Task Project<TEvent>(ConsumeContext<TEvent> ctx, string status)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
            ["Status"] = status,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            await _store.IncrementAsync(Name, evt.EventId, evt.OccurredOn, status, ct);

            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            _logger.LogDebug("Incremented {Status} in bucket for {OccurredOn:O}", status, evt.OccurredOn);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient projection failure — will retry");
            throw;
        }
        catch (Exception ex)
        {
            _metrics.RecordPermanentFailure(Name, typeof(TEvent).Name);
            _logger.LogError(ex,
                "Permanent projection failure for {EventType} {EventId} — event dropped",
                typeof(TEvent).Name, evt.EventId);
        }
    }

    private static bool IsTransient(Exception ex) => ex is
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException or
        TimeoutException or
        TaskCanceledException;
}
