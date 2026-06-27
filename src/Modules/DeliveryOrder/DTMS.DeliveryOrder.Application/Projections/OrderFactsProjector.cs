using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Application.Projections;

/// <summary>
/// Phase P5 — Materializes the bi.OrderFacts BI fact table. Subscribes
/// to the same 11 Order lifecycle events as
/// <c>OrderStatusHistoryProjector</c>, but instead of appending a row
/// per transition it stamps the matching timestamp column on a single
/// row per order. Result: report queries are O(rows) scans with no
/// JOINs — analyst self-service without touching the write side.
///
/// <para>The row is created lazily on the Confirmed event (the first
/// event in the lifecycle that carries dimensional data). Events
/// arriving for an order that hasn't been Confirmed are no-ops — the
/// backfill SQL handles pre-P5 history.</para>
/// </summary>
public class OrderFactsProjector :
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
    public const string Name = nameof(OrderFactsProjector);

    private readonly IOrderFactsProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<OrderFactsProjector> _logger;

    public OrderFactsProjector(
        IOrderFactsProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<OrderFactsProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1> ctx)
        => Run(ctx, () =>
        {
            var m = ctx.Message;
            return _store.UpsertOnConfirmAsync(
                m.DeliveryOrderId, m.OccurredOn, m.Priority, m.RequestedTransportMode,
                totalItems: m.Items.Count,
                totalWeightKg: m.Items.Sum(i => i.WeightKg),
                ctx.CancellationToken);
        });

    public Task Consume(ConsumeContext<DeliveryOrderDispatchedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetDispatchedAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderInProgressIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetInProgressAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderCompletedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetCompletedAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderPartiallyCompletedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetPartiallyCompletedAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderFailedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetFailedAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.Message.Reason, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderCancelledIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetCancelledAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.Message.Reason, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderRejectedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetRejectedAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.Message.Reason, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderHeldIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetHeldAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.Message.Reason, ctx.CancellationToken));

    public Task Consume(ConsumeContext<DeliveryOrderReleasedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetReleasedAtAsync(ctx.Message.DeliveryOrderId, ctx.Message.OccurredOn, ctx.CancellationToken));

    private async Task Run<TEvent>(ConsumeContext<TEvent> ctx, Func<Task> body)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ctx.CancellationToken))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            return;
        }

        try
        {
            await body();
            await _store.MarkProcessedAsync(Name, evt.EventId, ctx.CancellationToken);
            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);
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
