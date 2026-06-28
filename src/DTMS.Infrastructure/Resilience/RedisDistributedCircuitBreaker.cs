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
    private const string AllowScript = @"
local state = redis.call('HGET', KEYS[1], 'state')
local now = tonumber(ARGV[1])
local breakSec = tonumber(ARGV[2])

if state == false or state == 'closed' then
    return 1
end

if state == 'open' then
    local openedAt = tonumber(redis.call('HGET', KEYS[1], 'openedAt') or '0')
    if (now - openedAt) > breakSec then
        redis.call('HSET', KEYS[1], 'state', 'half-open')
        return 1
    end
    return 0
end

if state == 'half-open' then
    return 1
end

return 1
";

    // KEYS[1] = hash key, ARGV[1] = '1' for success / '0' for failure,
    // ARGV[2] = failure threshold, ARGV[3] = break duration seconds,
    // ARGV[4] = now (unix seconds).
    private const string RecordScript = @"
local success = ARGV[1] == '1'
local failureThreshold = tonumber(ARGV[2])
local breakSec = tonumber(ARGV[3])
local now = tonumber(ARGV[4])

if success then
    redis.call('HSET', KEYS[1], 'state', 'closed')
    redis.call('HSET', KEYS[1], 'failures', '0')
    redis.call('HDEL', KEYS[1], 'openedAt')
else
    local state = redis.call('HGET', KEYS[1], 'state')
    if state == 'half-open' then
        redis.call('HSET', KEYS[1], 'state', 'open')
        redis.call('HSET', KEYS[1], 'openedAt', tostring(now))
        redis.call('HSET', KEYS[1], 'failures', tostring(failureThreshold))
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
