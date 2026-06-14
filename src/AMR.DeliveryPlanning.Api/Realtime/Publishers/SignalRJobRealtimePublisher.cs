using AMR.DeliveryPlanning.Api.Realtime.Hubs;
using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using AMR.DeliveryPlanning.Planning.Application.Projections;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Publishers;

public sealed class SignalRJobRealtimePublisher : IJobRealtimePublisher
{
    private readonly IHubContext<JobHub, IJobClient> _hub;
    private readonly ILogger<SignalRJobRealtimePublisher> _logger;

    public SignalRJobRealtimePublisher(
        IHubContext<JobHub, IJobClient> hub,
        ILogger<SignalRJobRealtimePublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishTimelineUpdatedAsync(
        Guid jobId,
        JobTimelineEntryDto entry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(JobHub.GroupKey(jobId))
                .TimelineUpdated(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push TimelineUpdated for Job {JobId} — UI will catch up on next REST refresh",
                jobId);
        }
    }
}
