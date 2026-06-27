using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Hubs;

/// <summary>
/// Job-scoped realtime + the cross-order job queue. Two subscription
/// flavours so the Jobs queue page (broad) and a single Job drawer
/// (focused) can coexist without paying for each other's traffic.
/// </summary>
[Authorize]
public sealed class JobHub : Hub<IJobClient>
{
    private const string QueueGroup = "job-queue";

    /// <summary>
    /// Jobs queue page subscribes here to receive
    /// <see cref="IJobClient.JobUpdated"/> / <c>JobAdded</c> / <c>JobRemoved</c>
    /// pushed by the Job lifecycle projector.
    /// </summary>
    public Task SubscribeQueue()
        => Groups.AddToGroupAsync(Context.ConnectionId, QueueGroup);

    public Task UnsubscribeQueue()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, QueueGroup);

    public Task SubscribeJob(Guid jobId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupKey(jobId));

    public Task UnsubscribeJob(Guid jobId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupKey(jobId));

    public static string GroupKey(Guid jobId) => $"job:{jobId:N}";
    public static string QueueGroupKey => QueueGroup;
}
