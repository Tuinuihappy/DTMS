using DTMS.Dispatch.IntegrationEvents;
using DTMS.Planning.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Planning.Application.Consumers;

/// <summary>
/// Phase #1 — Mirror Trip pause onto the linked Job so the Jobs queue
/// and status timeline reflect the real lifecycle. Trip pause webhooks
/// only carry TripId, so we reverse-look-up via
/// <see cref="IJobRepository.GetByTripIdAsync"/>.
///
/// JobStatus deliberately stays a single <c>Paused</c> — the Hang/Held
/// flavour split lives at Trip level only; both flavours funnel into the
/// same MarkPaused.
///
/// Idempotent + safe under out-of-order webhooks — Job.MarkPaused
/// itself ignores duplicate/inappropriate-state calls.
/// </summary>
public class TripPausedJobConsumer :
    IConsumer<TripHangIntegrationEventV1>,
    IConsumer<TripHeldIntegrationEventV1>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripPausedJobConsumer> _logger;

    public TripPausedJobConsumer(IJobRepository jobRepository, ILogger<TripPausedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripHangIntegrationEventV1> context)
        => HandleAsync(context, context.Message.TripId, "TripHang");

    public Task Consume(ConsumeContext<TripHeldIntegrationEventV1> context)
        => HandleAsync(context, context.Message.TripId, "TripHeld");

    private async Task HandleAsync(ConsumeContext context, Guid tripId, string eventName)
    {
        var job = await _jobRepository.GetByTripIdAsync(tripId, context.CancellationToken);
        if (job is null)
        {
            // Common during legacy data — Trips that pre-date Phase b8
            // were never linked to a Job. Not a bug.
            _logger.LogDebug("[JobSync] {EventName} for Trip {TripId} — no linked Job", eventName, tripId);
            return;
        }

        job.MarkPaused(tripId);
        await _jobRepository.UpdateAsync(job, context.CancellationToken);
        _logger.LogInformation("[JobSync] Job {JobId} → Paused (mirrored from Trip {TripId} via {EventName})",
            job.Id, tripId, eventName);
    }
}
