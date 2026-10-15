using System.Text.Json;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Application.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// The single code path that refreshes one system's outbound callback token,
/// shared by the background loop (<c>force:false</c>, every due system) and the
/// manual endpoint (<c>force:true</c>, one system now).
///
/// <para>Safety: a Redis per-key lock serialises the manual (API-tier) and
/// background (worker-tier) writers so they can't both mint+save the same row;
/// the fresh reload happens inside the lock. The xmin concurrency token is the
/// backstop if the lock ever lapses — and when Redis is unreachable the refresh
/// runs without the lock on that backstop alone (fail-open, like the tiered
/// cache): an expired token stops every callback, a duplicate mint costs
/// nothing. A perpetual token is never silently
/// downgraded, and a newly minted token replaces the current one only when it is
/// strictly newer (or itself perpetual). Mint failures leave the working token
/// untouched.</para>
/// </summary>
public sealed class CallbackTokenRefresher : ICallbackTokenRefresher
{
    private const string LockKeyPrefix = "iam:refresh-lock:";
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ISystemCredentialRepository _creds;
    private readonly ICallbackTokenMinter _minter;
    private readonly CachedCredentialReader _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly CallbackRefreshMetrics _metrics;
    private readonly IOptionsMonitor<CallbackTokenRefreshOptions> _options;
    private readonly ILogger<CallbackTokenRefresher> _log;

    public CallbackTokenRefresher(
        ISystemCredentialRepository creds,
        ICallbackTokenMinter minter,
        CachedCredentialReader cache,
        IConnectionMultiplexer redis,
        CallbackRefreshMetrics metrics,
        IOptionsMonitor<CallbackTokenRefreshOptions> options,
        ILogger<CallbackTokenRefresher> log)
    {
        _creds = creds;
        _minter = minter;
        _cache = cache;
        _redis = redis;
        _metrics = metrics;
        _options = options;
        _log = log;
    }

    public async Task<RefreshResult> RefreshAsync(string systemKey, bool force, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var lockKey = LockKeyPrefix + systemKey;
        var lockToken = Guid.NewGuid().ToString("N");

        // SET NX EX — compare-and-delete on release so we never drop a lock a
        // later writer acquired after ours expired.
        bool locked;
        try
        {
            locked = await db.LockTakeAsync(lockKey, lockToken, LockTtl);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            // Redis down: refresh anyway, xmin guards the save (see class doc).
            _log.LogWarning(ex, "Redis unavailable taking the refresh lock for system={System}; refreshing without it", systemKey);
            var unlocked = await RefreshLockedAsync(systemKey, force, ct);
            _metrics.Record(systemKey, unlocked.Outcome.ToString().ToLowerInvariant());
            return unlocked;
        }

        if (!locked)
        {
            _metrics.Record(systemKey, "lock_busy");
            return RefreshResult.LockBusy();
        }

        try
        {
            var result = await RefreshLockedAsync(systemKey, force, ct);
            _metrics.Record(systemKey, result.Outcome.ToString().ToLowerInvariant());
            return result;
        }
        finally
        {
            try
            {
                await db.LockReleaseAsync(lockKey, lockToken);
            }
            catch (Exception ex) when (IsRedisUnavailable(ex))
            {
                // Redis went away mid-refresh; the lock's TTL frees it.
            }
        }
    }

    // RedisTimeoutException does not derive from RedisException, so both.
    private static bool IsRedisUnavailable(Exception ex)
        => ex is RedisException or RedisTimeoutException;

    private async Task<RefreshResult> RefreshLockedAsync(string systemKey, bool force, CancellationToken ct)
    {
        // Reload fresh INSIDE the lock — the value we filtered on may be stale.
        var cred = await _creds.GetBySystemKeyAsync(systemKey, ct);
        if (cred is null)
            return RefreshResult.Skipped($"No credential for '{systemKey}'.");

        var settings = TokenRefreshSettings.TryParse(cred.TokenRefreshConfig);
        if (settings is null)
            return RefreshResult.Skipped("Auto-refresh not configured.");

        // Enabled only switches the background loop off. A manual "Refresh now"
        // (force:true) is the operator's explicit ask to mint — honour it even
        // while auto-refresh is disabled, same as force already bypasses the
        // due check.
        if (!settings.Enabled && !force)
            return RefreshResult.Skipped("Auto-refresh disabled.");

        var hasCurrentToken = CallbackTokenInspector.ReadStoredToken(cred.CallbackAuthConfig) is not null;
        var currentExp = CallbackTokenInspector.ReadExpiryFromConfig(cred.CallbackAuthConfig);
        _metrics.RecordExpiry(systemKey, currentExp);

        switch (RefreshPolicy.Evaluate(hasCurrentToken, currentExp, force, settings.RefreshBeforeSeconds, DateTime.UtcNow))
        {
            case MintDecision.RejectPerpetual:
                return RefreshResult.Rejected("Current token never expires — refusing to replace it with an expiring one.");
            case MintDecision.Skip:
                return RefreshResult.Skipped(
                    hasCurrentToken && currentExp is null ? "Perpetual token — nothing to refresh." : "Not due yet.");
            case MintDecision.Mint:
            default:
                break;
        }

        // Mint. A failure here must NOT touch the stored token.
        string newJwt;
        try
        {
            using var mintCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            mintCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.MintTimeoutSeconds)));
            newJwt = await _minter.MintAsync(settings, mintCts.Token);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mint failed for system={System}; keeping existing token", systemKey);
            return RefreshResult.Failed($"Mint failed: {ex.Message}");
        }

        var newExp = CallbackTokenInspector.ReadExpFromBareJwt(newJwt);

        // Keep the minted token only when it's an improvement (later exp, or
        // perpetual). Otherwise leave the current one in place.
        if (!RefreshPolicy.AcceptsMinted(currentExp, newExp))
            return RefreshResult.Rejected("Minted token is not newer than the current one.");

        cred.SetCallback(
            baseUrl: cred.CallbackBaseUrl,
            authScheme: "bearer",
            authConfig: JsonSerializer.Serialize(new { token = newJwt }, JsonOpts),
            timeoutMs: cred.CallbackTimeoutMs);

        try
        {
            await _creds.UpdateAsync(cred, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer changed the row between our reload and save. Don't
            // clobber — the next tick (or retry) re-evaluates against fresh state.
            _log.LogInformation(
                "Concurrent update for system={System}; skipping this refresh", systemKey);
            return RefreshResult.Skipped("Concurrent update — will re-evaluate next tick.");
        }

        _metrics.RecordExpiry(systemKey, newExp);

        try
        {
            await _cache.InvalidateAsync(systemKey, ct);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            // The new token is saved; the tiered cache queued this invalidation
            // and replays it when Redis is back. Until then other processes may
            // serve the old token from cache — still valid, it hasn't expired.
            _log.LogWarning(ex, "Saved new token for system={System} but couldn't invalidate the cache; it will replay", systemKey);
        }

        _log.LogInformation(
            "Refreshed outbound token for system={System}; new exp={Exp}",
            systemKey, newExp?.ToString("o") ?? "(perpetual)");
        return RefreshResult.Refreshed(newExp);
    }
}
