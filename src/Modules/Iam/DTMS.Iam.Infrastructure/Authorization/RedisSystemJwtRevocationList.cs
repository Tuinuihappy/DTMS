using DTMS.Iam.Application.Authorization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DTMS.Iam.Infrastructure.Authorization;

/// <summary>
/// Redis-backed implementation of <see cref="ISystemJwtRevocationList"/>.
/// Keys: <c>iam:revoked-jti:{jti}</c>, value: sentinel <c>"1"</c>, TTL:
/// remaining token lifetime. Once the natural exp passes, Redis drops
/// the key on its own — no cleanup job needed.
///
/// <para>Fail-close: exceptions propagate; SystemJwtValidator wraps in
/// try/catch and rejects the request. See the interface comment for the
/// rationale.</para>
/// </summary>
public sealed class RedisSystemJwtRevocationList : ISystemJwtRevocationList
{
    private const string KeyPrefix = "iam:revoked-jti:";
    private static readonly RedisValue Sentinel = "1";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSystemJwtRevocationList> _log;

    public RedisSystemJwtRevocationList(
        IConnectionMultiplexer redis,
        ILogger<RedisSystemJwtRevocationList> log)
    {
        _redis = redis;
        _log = log;
    }

    public async Task RevokeAsync(string jti, DateTime? expiresAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            throw new ArgumentException("jti required.", nameof(jti));

        var db = _redis.GetDatabase();

        // Phase S.8d — perpetual token (no exp): write with NO TTL so the
        // blocklist entry never self-drops. The DB row is the durable
        // record; this Redis key is the hot-path fast reject.
        if (expiresAt is not DateTime exp)
        {
            await db.StringSetAsync(KeyPrefix + jti, Sentinel);
            _log.LogInformation(
                "Perpetual system JWT jti={Jti} added to revocation list (no TTL)", jti);
            return;
        }

        // TTL = remaining lifetime. Clamp to a minimum so a token that
        // expires in the next second doesn't get a negative/zero TTL
        // (Redis rejects those). A 1-minute floor is fine — the token
        // itself will exp inside that window and validator checks exp.
        var ttl = exp - DateTime.UtcNow;
        if (ttl < TimeSpan.FromMinutes(1))
            ttl = TimeSpan.FromMinutes(1);

        await db.StringSetAsync(KeyPrefix + jti, Sentinel, ttl);

        _log.LogInformation(
            "System JWT jti={Jti} added to revocation list (ttl {TtlSeconds}s)",
            jti, (int)ttl.TotalSeconds);
    }

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            // No jti on the token = we can't blocklist it. Treat as not
            // revoked; validator's other checks (signature/exp/iss/aud)
            // still apply. This branch exists only for defensive JWT-
            // shape validation upstream — SystemJwtIssuer always stamps
            // a jti.
            return false;

        var db = _redis.GetDatabase();
        // KeyExistsAsync avoids fetching the (empty) value — one round-trip.
        return await db.KeyExistsAsync(KeyPrefix + jti);
    }
}
