using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Projections;

/// <summary>
/// Phase P1 — Materializes Job status transitions into the
/// planning.JobStatusHistory read model. Subscribes to every Planning
/// integration event whose semantics imply a status change.
///
/// JobAssigned is currently skipped — see PlanningDomainEventMapper for
/// the reason (legacy event shape doesn't carry the data the projector
/// needs). Add the Assigned step when an operator use case requires it.
///
/// Idempotent + out-of-order safe (same rules as the Order side — see
/// docs/event-projection-plan.md decision log).
/// </summary>
public class JobStatusHistoryProjector :
    IConsumer<JobCreatedIntegrationEventV1>,
    IConsumer<PlanCommittedIntegrationEvent>,
    IConsumer<JobDispatchedIntegrationEventV1>,
    IConsumer<JobExecutingIntegrationEventV1>,
    IConsumer<JobCompletedIntegrationEventV1>,
    IConsumer<JobFailedIntegrationEventV1>,
    IConsumer<JobCancelledIntegrationEventV1>,
    IConsumer<JobPausedIntegrationEventV1>,
    IConsumer<JobResumedIntegrationEventV1>
{
    public const string Name = nameof(JobStatusHistoryProjector);

    private readonly IJobStatusHistoryProjectionStore _store;
    private readonly ProjectionMetrics _metrics;
    private readonly IJobRealtimePublisher _realtime;
    private readonly ILogger<JobStatusHistoryProjector> _logger;

    public JobStatusHistoryProjector(
        IJobStatusHistoryProjectionStore store,
        ProjectionMetrics metrics,
        IJobRealtimePublisher realtime,
        ILogger<JobStatusHistoryProjector> logger)
    {
        _store = store;
        _metrics = metrics;
        _realtime = realtime;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<JobCreatedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Created", reason: null);

    public Task Consume(ConsumeContext<PlanCommittedIntegrationEvent> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Committed",
            reason: ctx.Message.VehicleId.HasValue ? $"vehicle {ctx.Message.VehicleId}" : null);

    public Task Consume(ConsumeContext<JobDispatchedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Dispatched",
            reason: ctx.Message.VendorOrderKey is { Length: > 0 } v
                ? $"vendor={v} attempt={ctx.Message.AttemptNumber}"
                : $"attempt={ctx.Message.AttemptNumber}");

    public Task Consume(ConsumeContext<JobExecutingIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Executing",
            reason: $"trip {ctx.Message.TripId}");

    public Task Consume(ConsumeContext<JobCompletedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Completed",
            reason: $"trip {ctx.Message.TripId}");

    public Task Consume(ConsumeContext<JobFailedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Failed",
            reason: $"attempt={ctx.Message.AttemptNumber}: {ctx.Message.Reason}");

    public Task Consume(ConsumeContext<JobCancelledIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Cancelled", ctx.Message.Reason);

    public Task Consume(ConsumeContext<JobPausedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Paused",
            reason: $"trip {ctx.Message.TripId}");

    public Task Consume(ConsumeContext<JobResumedIntegrationEventV1> ctx)
        => Project(ctx, ctx.Message.JobId, ctx.Message.DeliveryOrderId, "Executing",
            reason: $"resumed from trip {ctx.Message.TripId}");

    private async Task Project<TEvent>(
        ConsumeContext<TEvent> ctx, Guid jobId, Guid deliveryOrderId,
        string toStatus, string? reason)
        where TEvent : class, IIntegrationEvent
    {
        var evt = ctx.Message;
        var ct = ctx.CancellationToken;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Projector"] = Name,
            ["EventId"] = evt.EventId,
            ["EventType"] = typeof(TEvent).Name,
            ["JobId"] = jobId,
        });

        if (await _store.HasProcessedEventAsync(Name, evt.EventId, ct))
        {
            _metrics.RecordDedupSkipped(Name, typeof(TEvent).Name);
            _logger.LogDebug("Skipped duplicate event {EventId}", evt.EventId);
            return;
        }

        try
        {
            var latest = await _store.GetLatestForJobAsync(jobId, ct);

            if (latest is { } prev && evt.OccurredOn < prev.OccurredAt)
            {
                _metrics.RecordPermanentFailure(Name, typeof(TEvent).Name);
                _logger.LogWarning(
                    "Out-of-order event {EventId} for Job {JobId} skipped " +
                    "(event time {EventTime:O} < latest recorded {LatestTime:O})",
                    evt.EventId, jobId, evt.OccurredOn, prev.OccurredAt);
                return;
            }

            var fromStatus = latest?.ToStatus;

            await _store.AppendAsync(
                Name, evt.EventId, jobId, deliveryOrderId,
                fromStatus, toStatus, evt.OccurredOn, reason, ct);

            _metrics.RecordProjected(Name, typeof(TEvent).Name);
            _metrics.RecordLag(Name, evt.OccurredOn);

            // Phase P1 — push to job:{id:N} SignalR group after durable write.
            _ = _realtime.PublishTimelineUpdatedAsync(
                jobId,
                new JobTimelineEntryDto(
                    EventId: evt.EventId,
                    JobId: jobId,
                    DeliveryOrderId: deliveryOrderId == Guid.Empty ? null : deliveryOrderId,
                    FromStatus: fromStatus,
                    ToStatus: toStatus,
                    OccurredAt: evt.OccurredOn,
                    Reason: reason),
                ct);

            _logger.LogInformation(
                "Projected {EventType} for Job {JobId}: {From}→{To}",
                typeof(TEvent).Name, jobId, fromStatus ?? "(initial)", toStatus);
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
