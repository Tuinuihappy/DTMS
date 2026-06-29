namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Read-side façade for "who subscribes to this event type, right now?"
/// — sits between the fan-out producer and the database. Implementations
/// are expected to cache aggressively (per-process snapshot,
/// Redis-pub/sub invalidation) so the producer hot path runs in memory
/// regardless of how many integration events fire per second.
/// </summary>
public interface ISubscriptionLookup
{
    /// <summary>
    /// Active subscribers for the given event type. Returns an empty
    /// list if no subscriber matches; never null. Disabled
    /// subscriptions are filtered out by the loader.
    /// </summary>
    Task<IReadOnlyList<EventSubscriber>> GetSubscribersAsync(string eventType, CancellationToken ct);

    /// <summary>
    /// Drop the cached entry for this event type (or all entries when
    /// <paramref name="eventType"/> is null). Called by the admin
    /// mutation flow and by the Redis pub/sub subscriber when another
    /// pod mutates a subscription.
    /// </summary>
    void Invalidate(string? eventType = null);
}

/// <summary>
/// Minimal subscriber projection. Carries only what the fan-out
/// producer needs to enqueue an outbox row — no entity reference, no
/// audit metadata. The CallbackBaseUrl + credentials are resolved at
/// dispatch time, not at producer time, so a URL rotation between
/// enqueue and dispatch goes through cleanly.
/// </summary>
public sealed record EventSubscriber(string SystemKey, string PayloadFormatKey);
