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
    /// <summary>
    /// Fail-open: a Redis failure (down, timeout) is reported as a miss
    /// (<c>null</c>), never thrown, so read-through callers fall back to
    /// their own store. Don't use this cache as a security control.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Writes L1 and returns; the L2 write and peer broadcast happen in the
    /// background and are best-effort (a Redis failure is logged, not thrown).
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Remove from L1 + L2 and broadcast to peers via the invalidation
    /// channel. Safe to call for keys that don't exist. Unlike Get/Set this
    /// THROWS on a Redis failure — the caller changed the data and must know
    /// peers may still hold the old value; the key is replayed on reconnect.
    /// </summary>
    Task InvalidateAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Cache-aside with cross-pod stampede protection. On miss, acquires
    /// a Redis SETNX lock at <paramref name="lockKey"/> so only one pod
    /// in the cluster calls <paramref name="factory"/>; the others wait
    /// up to <paramref name="waitTimeout"/> and read the value the winner
    /// wrote. Use for expensive factories — external token fetch, large
    /// query — where N pods × M concurrent requests would otherwise
    /// stampede the backing system.
    /// </summary>
    /// <param name="lockTimeout">
    /// SETNX expiry for the lock key. Caller MUST size this at or above
    /// the realistic worst-case factory duration. If the factory runs
    /// longer than this, the lock evicts and a second caller becomes a
    /// concurrent winner — two factory executions and a race on the
    /// final write.
    /// </param>
    /// <param name="waitTimeout">
    /// Upper bound on how long losers poll for the winner's published
    /// value. Should be at least <paramref name="lockTimeout"/> so a
    /// slow factory doesn't time out its own waiters.
    /// </param>
    Task<T> GetOrSetWithLockAsync<T>(
        string key,
        string lockKey,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        TimeSpan lockTimeout,
        TimeSpan waitTimeout,
        CancellationToken ct = default) where T : class;
}
