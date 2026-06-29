using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Application.Repositories;
using DTMS.SharedKernel.Caching;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Phase S.3.1b — tiered-cache implementation of
/// <see cref="ISubscriptionLookup"/>. Mirrors the
/// <see cref="DTMS.Iam.Application.Authorization.CachedCredentialReader"/>
/// shape: L1 per-pod, L2 Redis, admin mutation broadcasts an invalidate
/// through <c>ITieredCache</c> so every pod's L1 drops within one
/// round-trip.
///
/// <para>One cache entry per <c>EventType</c>. Filter at SQL (Enabled =
/// true) so cache never carries disabled rows — admin disabling a
/// subscription must invalidate.</para>
/// </summary>
public sealed class CachedSubscriptionLookup : ISubscriptionLookup
{
    public const string CacheKeyPrefix = "iam:sub:";
    private static readonly TimeSpan L2Ttl = TimeSpan.FromMinutes(5);

    private readonly ITieredCache _cache;
    private readonly ISystemEventSubscriptionRepository _repo;

    public CachedSubscriptionLookup(
        ITieredCache cache,
        ISystemEventSubscriptionRepository repo)
    {
        _cache = cache;
        _repo = repo;
    }

    public async Task<IReadOnlyList<EventSubscriber>> GetSubscribersAsync(
        string eventType, CancellationToken ct)
    {
        var key = CacheKeyPrefix + eventType;

        var cached = await _cache.GetAsync<EventSubscriber[]>(key, ct);
        if (cached is not null)
            return cached;

        var rows = await _repo.ListEnabledByEventTypeAsync(eventType, ct);
        var snapshot = rows
            .Select(r => new EventSubscriber(r.SystemKey, r.PayloadFormatKey))
            .ToArray();

        // Always cache, even the empty array — "no subscribers" is the
        // common case for new events and we don't want to thrash the DB.
        await _cache.SetAsync(key, snapshot, L2Ttl, ct);
        return snapshot;
    }

    public void Invalidate(string? eventType = null)
    {
        // ITieredCache is async-only; fire-and-forget here is fine because
        // the caller (admin mutation handler) awaits the broader DB
        // commit + Redis publish separately. Best-effort consistency:
        // L2 evict is what matters cross-pod; L1 falls through naturally
        // on the local pod via SetAsync's publish path.
        if (eventType is null)
        {
            // Wildcard invalidate isn't supported by the current
            // ITieredCache contract — admin "clear all" path is rare
            // enough that doing it per-known-event is acceptable.
            foreach (var et in CallbackEventTypes.All)
                _ = _cache.InvalidateAsync(CacheKeyPrefix + et, CancellationToken.None);
        }
        else
        {
            _ = _cache.InvalidateAsync(CacheKeyPrefix + eventType, CancellationToken.None);
        }
    }
}
