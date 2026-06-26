using AMR.DeliveryPlanning.SharedKernel.Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.SharedKernel.Projection;

/// <summary>
/// Base class for every Event Projection consumer in DTMS. Handles three
/// cross-cutting concerns once so subclasses only implement the mapping
/// from event to read-model row:
///
/// 1. <b>Idempotency</b> — short-circuits when the (projector, EventId)
///    pair already exists in the module's projection_inbox table. The
///    inbox row is written in the SAME SaveChanges call as the read-model
///    row, so at-least-once delivery becomes effectively-once.
///
/// 2. <b>Lag observability</b> — records
///    <c>dtms.projection.lag_seconds{projector}</c> and
///    <c>dtms.projection.events_projected_total{projector,event_type}</c>
///    via <see cref="ProjectionMetrics"/> on every successful project.
///    Skipped duplicates record
///    <c>dtms.projection.dedup_skipped_total</c>.
///
/// 3. <b>Structured logging</b> — uniform log scopes attach
///    {Projector}, {EventId}, {EventType} to downstream log entries.
///
/// Subclasses MUST:
/// - implement <see cref="ProjectAsync"/> with the read-model write
/// - call SaveChangesAsync on the DbContext that owns BOTH the read-model
///   table AND the projection_inbox row, so atomicity holds
/// - extract the aggregate id via <see cref="GetAggregateId"/> when
///   per-aggregate ordering matters (sub-class override)
/// </summary>
public abstract class IdempotentProjector<TEvent> : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    private readonly IProjectionInboxRepository _inbox;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger _logger;

    protected IdempotentProjector(
        IProjectionInboxRepository inbox,
        ProjectionMetrics metrics,
        ILogger logger)
    {
        _inbox = inbox;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Subclass uniquely names itself for inbox + metrics. Defaults to
    /// the runtime type name — override only when renaming a projector
    /// while preserving the existing inbox history.
    /// </summary>
    protected virtual string ProjectorName => GetType().Name;

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = ProjectorName,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
        });

        if (await _inbox.HasProcessedAsync(ProjectorName, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(ProjectorName, typeof(TEvent).Name);
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            await ProjectAsync(evt, ct);
            await _inbox.RecordAsync(
                new InboxMessage(ProjectorName, evt.EventId, DateTime.UtcNow), ct);
            await SaveChangesAsync(ct);

            _metrics.RecordProjected(ProjectorName, typeof(TEvent).Name);
            _metrics.RecordLag(ProjectorName, evt.OccurredOn);

            _logger.LogInformation(
                "Projected {EventType} {EventId} (lag {LagMs}ms)",
                typeof(TEvent).Name, evt.EventId,
                (long)(DateTime.UtcNow - evt.OccurredOn).TotalMilliseconds);
        }
        catch (Exception ex) when (!IsTransient(ex))
        {
            // Permanent failure — don't block the queue. Future: route to
            // DLQ inspection UI (deferred from P0). Log richly so ops can
            // diagnose without a debugger.
            _metrics.RecordPermanentFailure(ProjectorName, typeof(TEvent).Name);
            _logger.LogError(ex,
                "Permanent projection failure for {EventType} {EventId} — event will not be retried",
                typeof(TEvent).Name, evt.EventId);
        }
    }

    /// <summary>
    /// Write the read-model row(s) corresponding to this event. The base
    /// class records the inbox row + calls SaveChangesAsync afterwards,
    /// so subclass implementations should NOT call SaveChanges themselves.
    /// </summary>
    protected abstract Task ProjectAsync(TEvent evt, CancellationToken cancellationToken);

    /// <summary>
    /// Commit the read-model + inbox row in one transaction. Default
    /// implementation throws — subclasses inject a DbContext and override
    /// to call its SaveChangesAsync. This keeps the base class
    /// DbContext-agnostic so the same pattern works across all 6 modules.
    /// </summary>
    protected abstract Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Distinguish transient (deserves retry) from permanent (skip + log)
    /// failures. Default policy:
    ///   - DbUpdateConcurrencyException → transient
    ///   - TimeoutException / TaskCanceledException → transient
    ///   - everything else → permanent
    /// Override for projector-specific classification.
    /// </summary>
    protected virtual bool IsTransient(Exception ex) => ex is
        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException or
        TimeoutException or
        TaskCanceledException;
}
