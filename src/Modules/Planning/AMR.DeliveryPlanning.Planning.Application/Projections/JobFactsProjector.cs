using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Projections;

/// <summary>
/// Phase P5.2 — Materializes the bi.JobFacts BI fact table. JobCreated
/// births the row; subsequent lifecycle events stamp the matching
/// timestamp column.
/// </summary>
public class JobFactsProjector :
    IConsumer<JobCreatedIntegrationEventV1>,
    IConsumer<JobAssignedIntegrationEvent>,
    IConsumer<PlanCommittedIntegrationEvent>,
    IConsumer<JobDispatchedIntegrationEventV1>,
    IConsumer<JobExecutingIntegrationEventV1>,
    IConsumer<JobCompletedIntegrationEventV1>,
    IConsumer<JobFailedIntegrationEventV1>,
    IConsumer<JobCancelledIntegrationEventV1>
{
    public const string Name = nameof(JobFactsProjector);

    private readonly IJobFactsProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly ILogger<JobFactsProjector> _logger;

    public JobFactsProjector(
        IJobFactsProjectionStore store,
        ProjectionMetrics metrics,
        ILogger<JobFactsProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<JobCreatedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.UpsertOnCreatedAsync(
            ctx.Message.JobId, ctx.Message.DeliveryOrderId,
            ctx.Message.OccurredOn, ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobAssignedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetAssignedAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.VehicleId == Guid.Empty ? null : ctx.Message.VehicleId,
            ctx.CancellationToken));

    public Task Consume(ConsumeContext<PlanCommittedIntegrationEvent> ctx)
        => Run(ctx, () => _store.SetCommittedAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.VehicleId, ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobDispatchedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetDispatchedAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.TripId == Guid.Empty ? null : ctx.Message.TripId,
            ctx.Message.VendorOrderKey, ctx.Message.AttemptNumber,
            ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobExecutingIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetExecutingAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.TripId == Guid.Empty ? null : ctx.Message.TripId,
            ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobCompletedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetCompletedAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.TripId == Guid.Empty ? null : ctx.Message.TripId,
            ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobFailedIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetFailedAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.Reason, ctx.Message.AttemptNumber,
            // V1.1 — pre-V1.1 events have null, store defaults to "None".
            ctx.Message.FailureCategory,
            ctx.CancellationToken));

    public Task Consume(ConsumeContext<JobCancelledIntegrationEventV1> ctx)
        => Run(ctx, () => _store.SetCancelledAtAsync(
            ctx.Message.JobId, ctx.Message.OccurredOn,
            ctx.Message.TripId == Guid.Empty ? null : ctx.Message.TripId,
            ctx.Message.Reason,
            ctx.Message.FailureCategory,
            ctx.CancellationToken));

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
