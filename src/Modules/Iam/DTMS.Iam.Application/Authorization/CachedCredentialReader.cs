using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Application.Security;
using DTMS.Iam.Domain.Entities;
using DTMS.SharedKernel.Caching;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Tiered-cache front for <see cref="ISystemCredentialRepository"/>.
/// Every inbound request from a system principal lands here once;
/// L1 (per-pod, 60s) handles the burst, L2 (Redis, 5 min) absorbs
/// cross-pod traffic, and admin rotate endpoints (Phase S.4) call
/// <see cref="InvalidateAsync"/> to broadcast the change so every
/// pod drops its L1 entry within one Redis round-trip.
/// </summary>
public sealed class CachedCredentialReader
{
    public const string CacheKeyPrefix = "iam:cred:";
    private static readonly TimeSpan L2Ttl = TimeSpan.FromMinutes(5);

    private readonly ITieredCache _cache;
    private readonly ISystemCredentialRepository _repo;
    private readonly ICallbackTokenProtector _protector;

    public CachedCredentialReader(
        ITieredCache cache,
        ISystemCredentialRepository repo,
        ICallbackTokenProtector protector)
    {
        _cache = cache;
        _repo = repo;
        _protector = protector;
    }

    public async Task<CachedCredential?> GetAsync(string systemKey, CancellationToken ct = default)
    {
        var key = CacheKeyPrefix + systemKey;

        // Encrypt-at-rest — the cached DTO carries CallbackAuthConfig as
        // ciphertext (Redis persists to the redis-data volume, so a cache
        // entry is at-rest too). Callers always get a decrypted COPY: the
        // L1 tier hands back the same object instance it stored, so
        // mutating the cached DTO would corrupt what later hits read.
        var cached = await _cache.GetAsync<CachedCredential>(key, ct);
        if (cached is not null)
            return cached.WithCallbackAuthConfig(_protector.TryUnprotect(cached.CallbackAuthConfig));

        var row = await _repo.GetBySystemKeyAsync(systemKey, ct);
        if (row is null)
            return null;

        // The entity already went through the EF converter, so its
        // CallbackAuthConfig is plaintext here — re-protect for the cache.
        var dto = CachedCredential.FromEntity(row);
        await _cache.SetAsync(key, dto.WithCallbackAuthConfig(_protector.Protect(dto.CallbackAuthConfig)), L2Ttl, ct);
        return dto;
    }

    public Task InvalidateAsync(string systemKey, CancellationToken ct = default)
        => _cache.InvalidateAsync(CacheKeyPrefix + systemKey, ct);
}

/// <summary>
/// Cache-friendly snapshot of <see cref="SystemCredential"/>. Plain
/// POCO so System.Text.Json can serialise it for the L2 (Redis)
/// store. AuthConfig + CallbackAuthConfig are kept as raw JSON
/// strings; the auth-scheme handler parses the shape it expects.
/// </summary>
public sealed class CachedCredential
{
    public string SystemKey { get; set; } = string.Empty;
    public string AuthScheme { get; set; } = string.Empty;
    public string AuthConfig { get; set; } = string.Empty;
    public string? CallbackBaseUrl { get; set; }
    public string? CallbackAuthScheme { get; set; }
    public string? CallbackAuthConfig { get; set; }
    public int CallbackTimeoutMs { get; set; }
    public int RetryMaxAttempts { get; set; }
    public int CircuitFailureThreshold { get; set; }
    public int CircuitDurationSeconds { get; set; }

    public static CachedCredential FromEntity(SystemCredential e) => new()
    {
        SystemKey = e.SystemKey,
        AuthScheme = e.AuthScheme,
        AuthConfig = e.AuthConfig,
        CallbackBaseUrl = e.CallbackBaseUrl,
        CallbackAuthScheme = e.CallbackAuthScheme,
        CallbackAuthConfig = e.CallbackAuthConfig,
        CallbackTimeoutMs = e.CallbackTimeoutMs,
        RetryMaxAttempts = e.RetryMaxAttempts,
        CircuitFailureThreshold = e.CircuitFailureThreshold,
        CircuitDurationSeconds = e.CircuitDurationSeconds,
    };

    /// <summary>
    /// Copy with a different <see cref="CallbackAuthConfig"/> — used by the
    /// reader to swap plaintext↔ciphertext without mutating the instance
    /// the L1 cache holds.
    /// </summary>
    public CachedCredential WithCallbackAuthConfig(string? callbackAuthConfig) => new()
    {
        SystemKey = SystemKey,
        AuthScheme = AuthScheme,
        AuthConfig = AuthConfig,
        CallbackBaseUrl = CallbackBaseUrl,
        CallbackAuthScheme = CallbackAuthScheme,
        CallbackAuthConfig = callbackAuthConfig,
        CallbackTimeoutMs = CallbackTimeoutMs,
        RetryMaxAttempts = RetryMaxAttempts,
        CircuitFailureThreshold = CircuitFailureThreshold,
        CircuitDurationSeconds = CircuitDurationSeconds,
    };
}
