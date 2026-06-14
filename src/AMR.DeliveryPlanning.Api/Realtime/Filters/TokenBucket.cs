namespace AMR.DeliveryPlanning.Api.Realtime.Filters;

/// <summary>
/// Thread-safe token bucket — refills continuously at a constant rate
/// up to <see cref="_capacity"/>. Used per-connection by
/// <see cref="RateLimitedHubFilter"/> to cap method invocation rate
/// without blocking under bursts (caller drops the call instead).
///
/// Math:
///   - Capacity = burst budget (consumes allowed within one refill window).
///   - Refill = sustained rate (tokens per second).
///   - At t=0: full bucket. Consume 1 per allowed call.
///   - On each TryConsume call we lazy-refill based on elapsed time —
///     no background timer needed, so 1000s of buckets cost nothing
///     when idle.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _lock = new();
    private readonly int _capacity;
    private readonly double _refillPerMs;
    private double _tokens;
    private long _lastRefillTicks;

    public TokenBucket(int capacity, int refillPerSecond)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be > 0");
        if (refillPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(refillPerSecond), "refillPerSecond must be > 0");

        _capacity = capacity;
        _refillPerMs = refillPerSecond / 1000.0;
        _tokens = capacity;
        _lastRefillTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Attempt to remove 1 token. Returns true when the bucket had a token
    /// available, false when the caller should drop / reject the request.
    /// </summary>
    public bool TryConsume()
    {
        lock (_lock)
        {
            Refill();
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }

    /// <summary>
    /// Diagnostic snapshot — current token count. Exposed for tests +
    /// admin observability; production code should not branch on it.
    /// </summary>
    public double TokensAvailable
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _tokens;
            }
        }
    }

    private void Refill()
    {
        var now = Environment.TickCount64;
        var elapsedMs = now - _lastRefillTicks;
        if (elapsedMs <= 0) return;
        _tokens = Math.Min(_capacity, _tokens + elapsedMs * _refillPerMs);
        _lastRefillTicks = now;
    }
}
