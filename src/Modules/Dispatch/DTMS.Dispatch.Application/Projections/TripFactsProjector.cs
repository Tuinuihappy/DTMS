using DTMS.Dispatch.IntegrationEvents;
using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Projections;

/// <summary>
/// Phase P5.2 — Materializes the bi.TripFacts BI fact table.
/// Subscribes to the same 6 Trip lifecycle events as
/// <c>TripStatusHistoryProjector</c>. For trips that pre-date P5.2 the
/// backfill SQL seeds the row from dispatch.Trips + dispatch.TripStatusHistory;
/// from then on the projector keeps it current.
/// </summary>
public class TripFactsProjector :
    IConsumer<TripStartedIntegrationEvent>,
    IConsumer<TripPausedIntegrationEventV1>,
    IConsumer<TripResumedIntegrationEventV1>,
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>
{
    public const string Name = nameof(TripFactsProjector);

    private readonly ITripFactsProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<TripFactsProjector> _logger;

    public TripFactsProjector(
        ITripFactsProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<TripFactsProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripStartedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetStartedAtAsync(
            ctx.Message.TripId, ctx.Message.OccurredOn,
            ctx.Message.DeliveryOrderId == Guid.Empty ? null : ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            ctx.Message.VehicleId == Guid.Empty ? null : ctx.Message.VehicleId,
            ctx.Message.VendorVehicleKey,
            ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripPausedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.RecordPausedAsync(
            ctx.Message.TripId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripResumedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.RecordResumedAsync(
            ctx.Message.TripId, ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetCompletedAtAsync(
            ctx.Message.TripId, ctx.Message.OccurredOn,
            ctx.Message.DeliveryOrderId == Guid.Empty ? null : ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            ctx.Message.VendorUpperKey, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetFailedAtAsync(
            ctx.Message.TripId, ctx.Message.OccurredOn,
            ctx.Message.DeliveryOrderId == Guid.Empty ? null : ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            ctx.Message.VendorUpperKey, ctx.Message.Reason, ctx.CancellationToken));

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetCancelledAtAsync(
            ctx.Message.TripId, ctx.Message.OccurredOn,
            ctx.Message.DeliveryOrderId == Guid.Empty ? null : ctx.Message.DeliveryOrderId,
            ctx.Message.JobId == Guid.Empty ? null : ctx.Message.JobId,
            ctx.Message.VendorUpperKey, ctx.Message.Reason, ctx.CancellationToken));

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
