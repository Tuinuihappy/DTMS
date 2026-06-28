namespace DTMS.SharedKernel.Caching;

/// <summary>
/// Two-tier cache: L1 = in-process (per-pod, short TTL); L2 = shared
/// distributed cache (Redis, long TTL). Reads fall through L1 → L2 →
/// caller-supplied factory. Writes hit both tiers. Invalidation clears L1
/// locally, deletes L2, and broadcasts a message to every other pod so
/// they evict their L1 copy too. See Phase S.0 of the federated
/// source-system plan for the multi-replica reasoning.
/// </summary>
public interface ITieredCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Remove from L1 + L2 and broadcast to peers via the invalidation
    /// channel. Safe to call for keys that don't exist.
    /// </summary>
    Task InvalidateAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Cache-aside with cross-pod stampede protection. On miss, acquires
    /// a Redis SETNX lock at <paramref name="lockKey"/> so only one pod
    /// in the cluster calls <paramref name="factory"/>; the others wait
    /// (bounded by <paramref name="lockTimeout"/>) and read the value
    /// the winner wrote. Use for expensive factories — external token
    /// fetch, large query — where N pods × M concurrent requests would
    /// otherwise stampede the backing system.
    /// </summary>
    Task<T> GetOrSetWithLockAsync<T>(
        string key,
        string lockKey,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        TimeSpan lockTimeout,
        CancellationToken ct = default) where T : class;
}
