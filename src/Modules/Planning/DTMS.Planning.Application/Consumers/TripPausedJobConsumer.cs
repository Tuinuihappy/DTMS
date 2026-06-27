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
/// Idempotent + safe under out-of-order webhooks — Job.MarkPaused
/// itself ignores duplicate/inappropriate-state calls.
/// </summary>
public class TripPausedJobConsumer : IConsumer<TripPausedIntegrationEventV1>
{
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<TripPausedJobConsumer> _logger;

    public TripPausedJobConsumer(IJobRepository jobRepository, ILogger<TripPausedJobConsumer> logger)
    {
        _jobRepository = jobRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripPausedIntegrationEventV1> context)
    {
        var evt = context.Message;
        var job = await _jobRepository.GetByTripIdAsync(evt.TripId, context.CancellationToken);
        if (job is null)
        {
            // Common during legacy data — Trips that pre-date Phase b8
            // were never linked to a Job. Not a bug.
            _logger.LogDebug("[JobSync] TripPaused for Trip {TripId} — no linked Job", evt.TripId);
            return;
        }

        job.MarkPaused(evt.TripId);
        await _jobRepository.UpdateAsync(job, context.CancellationToken);
        _logger.LogInformation("[JobSync] Job {JobId} → Paused (mirrored from Trip {TripId})",
            job.Id, evt.TripId);
    }
}
