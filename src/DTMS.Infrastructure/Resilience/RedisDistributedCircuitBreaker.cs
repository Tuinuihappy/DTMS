using DTMS.SharedKernel.Resilience;
using StackExchange.Redis;

namespace DTMS.Infrastructure.Resilience;

/// <summary>
/// Redis-backed circuit breaker. Both gate-check and result-recording
/// run as Lua scripts so the read-decide-write cycle is atomic from the
/// perspective of all calling pods. Without atomicity, two pods that
/// both observe <c>state=open</c> at the same instant the break window
/// expires would each promote to <c>half-open</c> and fire a probe —
/// the very stampede the breaker exists to prevent.
/// </summary>
public sealed class RedisDistributedCircuitBreaker : IDistributedCircuitBreaker
{
    private readonly IConnectionMultiplexer _redis;

    // KEYS[1] = hash key, ARGV[1] = now (unix seconds), ARGV[2] = break duration seconds.
    // Returns 1 if call is allowed, 0 if open.
    //
    // Half-open probe gating: at most one in-flight probe at a time across
    // every replica. The state hash transitions open → half-open atomically
    // when the break window elapses, and an SETNX probe lock at
    // `<key>:probe` claims the single probe slot. Subsequent half-open
    // callers race for the same lock — losers return 0 so we never get a
    // multi-replica stampede on the still-broken backend.
    //
    // Lock TTL = probe duration ceiling. If the probe stalls without
    // recording a result, the lock expires and the next caller probes.
    // RecordScript releases the lock immediately on either outcome.
    private const string AllowScript = @"
local state = redis.call('HGET', KEYS[1], 'state')
local now = tonumber(ARGV[1])
local breakSec = tonumber(ARGV[2])
local ttl = breakSec * 4
if ttl < 3600 then ttl = 3600 end
local probeKey = KEYS[1] .. ':probe'
local probeTtl = 10

if state == false or state == 'closed' then
    return 1
end

if state == 'open' then
    local openedAt = tonumber(redis.call('HGET', KEYS[1], 'openedAt') or '0')
    if (now - openedAt) > breakSec then
        -- Transition to half-open and atomically claim the single probe
        -- slot. This caller is the one probe; siblings hitting the
        -- 'half-open' branch below will race for the lock after release.
        redis.call('HSET', KEYS[1], 'state', 'half-open')
        redis.call('SET', probeKey, '1', 'EX', probeTtl)
        redis.call('EXPIRE', KEYS[1], tostring(ttl))
        return 1
    end
    -- Still open within the break window. Refresh TTL so the breaker
    -- doesn't silently expire to 'closed' during a sustained outage
    -- where no caller ever records a result.
    redis.call('EXPIRE', KEYS[1], tostring(ttl))
    return 0
end

if state == 'half-open' then
    -- Race for the single probe slot. Winner returns 1; losers wait
    -- out the break window or until a Record* clears the state.
    local got = redis.call('SET', probeKey, '1', 'NX', 'EX', probeTtl)
    if got then
        redis.call('EXPIRE', KEYS[1], tostring(ttl))
        return 1
    end
    redis.call('EXPIRE', KEYS[1], tostring(ttl))
    return 0
end

return 1
";

    // KEYS[1] = hash key, ARGV[1] = '1' for success / '0' for failure,
    // ARGV[2] = failure threshold, ARGV[3] = break duration seconds,
    // ARGV[4] = now (unix seconds).
    //
    // Always clears the probe lock so a follow-on caller can probe
    // again immediately on the next half-open promotion. Without the
    // explicit DEL, the lock would linger up to its TTL and starve the
    // next probe.
    private const string RecordScript = @"
local success = ARGV[1] == '1'
local failureThreshold = tonumber(ARGV[2])
local breakSec = tonumber(ARGV[3])
local now = tonumber(ARGV[4])
local probeKey = KEYS[1] .. ':probe'

if success then
    redis.call('HSET', KEYS[1], 'state', 'closed')
    redis.call('HSET', KEYS[1], 'failures', '0')
    redis.call('HDEL', KEYS[1], 'openedAt')
    redis.call('DEL', probeKey)
else
    local state = redis.call('HGET', KEYS[1], 'state')
    if state == 'half-open' then
        redis.call('HSET', KEYS[1], 'state', 'open')
        redis.call('HSET', KEYS[1], 'openedAt', tostring(now))
        redis.call('HSET', KEYS[1], 'failures', tostring(failureThreshold))
        redis.call('DEL', probeKey)
    else
        local fails = tonumber(redis.call('HINCRBY', KEYS[1], 'failures', 1))
        if fails >= failureThreshold then
            redis.call('HSET', KEYS[1], 'state', 'open')
            redis.call('HSET', KEYS[1], 'openedAt', tostring(now))
        end
    end
end

-- TTL guard so quiet keys eventually evict; long enough that legitimate
-- open windows never expire mid-recovery.
local ttl = breakSec * 4
if ttl < 3600 then ttl = 3600 end
redis.call('EXPIRE', KEYS[1], tostring(ttl))
return 1
";

    public RedisDistributedCircuitBreaker(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> AllowAsync(string key, TimeSpan breakDuration, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        RedisResult result = await db.ScriptEvaluateAsync(
            AllowScript,
            keys: new RedisKey[] { key },
            values: new RedisValue[] { now, (long)breakDuration.TotalSeconds });

        return (long)result == 1L;
    }

    public async Task RecordResultAsync(
        string key,
        bool success,
        int failureThreshold,
        TimeSpan breakDuration,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await db.ScriptEvaluateAsync(
            RecordScript,
            keys: new RedisKey[] { key },
            values: new RedisValue[]
            {
                success ? "1" : "0",
                failureThreshold,
                (long)breakDuration.TotalSeconds,
                now,
            });
    }
}
