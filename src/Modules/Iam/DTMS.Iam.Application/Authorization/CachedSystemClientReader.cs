using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.SharedKernel.Caching;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Tiered-cache front for <see cref="ISystemClientRepository.GetByKeyAsync"/>.
/// Mirrors <see cref="CachedCredentialReader"/>: L1 (per-pod, 60s via
/// ITieredCache defaults) + L2 (Redis, 5 min) + <see cref="InvalidateAsync"/>
/// broadcast on admin write.
///
/// <para><b>Why a separate reader from CachedCredentialReader:</b>
/// <see cref="SystemClient"/> and <see cref="Domain.Entities.SystemCredential"/>
/// are separate aggregates. Both are queried in the same middleware
/// pipeline but their write cadences differ — credentials rotate
/// (invalidation frequent), client metadata is near-static (activation
/// toggle is the main mutation). Sharing one cache key would over-
/// invalidate the client side on every credential rotation.</para>
///
/// <para><b>Null caching:</b> negative results are NOT cached. A partner
/// onboarding just after the first request would otherwise get a stale
/// "unknown" until L2 TTL expires — worse UX than a re-read.</para>
/// </summary>
public sealed class CachedSystemClientReader
{
    public const string CacheKeyPrefix = "iam:client:";
    private static readonly TimeSpan L2Ttl = TimeSpan.FromMinutes(5);

    private readonly ITieredCache _cache;
    private readonly ISystemClientRepository _repo;

    public CachedSystemClientReader(ITieredCache cache, ISystemClientRepository repo)
    {
        _cache = cache;
        _repo = repo;
    }

    public async Task<CachedSystemClient?> GetAsync(string systemKey, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyPrefix + systemKey;

        var cached = await _cache.GetAsync<CachedSystemClient>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var row = await _repo.GetByKeyAsync(systemKey, ct);
        if (row is null)
            return null;

        var dto = CachedSystemClient.FromEntity(row);
        await _cache.SetAsync(cacheKey, dto, L2Ttl, ct);
        return dto;
    }

    public Task InvalidateAsync(string systemKey, CancellationToken ct = default)
        => _cache.InvalidateAsync(CacheKeyPrefix + systemKey, ct);
}

/// <summary>
/// Cache-friendly snapshot of <see cref="SystemClient"/>. Plain POCO so
/// System.Text.Json can serialise it for the L2 (Redis) store. Only the
/// fields hot-path callers need — hydrating the full domain entity for
/// every request would waste bytes on Redis and CPU on deserialise.
/// </summary>
public sealed class CachedSystemClient
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public static CachedSystemClient FromEntity(SystemClient e) => new()
    {
        Key = e.Key,
        DisplayName = e.DisplayName,
        IsActive = e.IsActive,
    };
}
