namespace DTMS.Api.Realtime.Hubs.Clients;

/// <summary>
/// Typed SignalR client for <see cref="JobHub"/> — used by the Jobs queue
/// page (b10-frontend.2) and Job detail drawer (P1).
/// </summary>
public interface IJobClient
{
    /// <summary>
    /// A new entry was appended to a Job's status timeline (Phase P1).
    /// </summary>
    Task TimelineUpdated(object entry);

    /// <summary>
    /// A single Job's overall status changed. Pushed to the
    /// <c>job-queue</c> group so the operator's queue page updates the
    /// row in place without a refetch.
    /// </summary>
    Task JobUpdated(object job);

    /// <summary>
    /// A new Job entered the queue (e.g., a freshly Failed job becoming
    /// retriable). Pushed to <c>job-queue</c>.
    /// </summary>
    Task JobAdded(object job);

    /// <summary>
    /// A Job left the queue (e.g., Completed) — pushed to <c>job-queue</c>
    /// so the row can be removed.
    /// </summary>
    Task JobRemoved(Guid jobId);
}
