using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace DTMS.Api.Infrastructure.Health;

// Redis readiness probe that REUSES the app's singleton IConnectionMultiplexer,
// the same move NpgsqlDataSourceHealthCheck made for postgres.
//
// The old inline check called ConnectionMultiplexer.Connect + Ping + Close
// synchronously on every /health/ready hit: a new connection per probe and a
// parked pool thread. With Redis down it took 5,001-5,007 ms (2026-10-02 stop
// test), past the container healthcheck's 5 s timeout, so probes failed on time
// alone. Now a disconnected multiplexer answers at once, and a connected one
// gets a 1 s ping budget.
//
// Degraded, never Unhealthy: the tiered cache is fail-open, so the API still
// serves without Redis (see HealthResponse for how Degraded maps to HTTP).
internal sealed class RedisMultiplexerHealthCheck : IHealthCheck
{
    private static readonly TimeSpan PingBudget = TimeSpan.FromSeconds(1);

    private readonly IConnectionMultiplexer _redis;

    public RedisMultiplexerHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_redis.IsConnected)
            return HealthCheckResult.Degraded("Redis is not connected; cache reads fall through to the database.");

        try
        {
            var latency = await _redis.GetDatabase().PingAsync().WaitAsync(PingBudget, cancellationToken);
            return HealthCheckResult.Healthy($"ping {latency.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"Redis ping failed: {ex.Message}");
        }
    }
}
