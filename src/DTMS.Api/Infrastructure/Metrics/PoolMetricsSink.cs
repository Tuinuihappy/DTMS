using DTMS.Transport.Manual.Application.Services;

namespace DTMS.Api.Infrastructure.Metrics;

/// <summary>
/// Api-side implementation of <see cref="IPoolMetricsSink"/> — the
/// Transport.Manual.Application module publishes claim outcomes through
/// this so the meter (which lives in Api) stays module-boundary-clean.
/// </summary>
public sealed class PoolMetricsSink : IPoolMetricsSink
{
    private readonly PoolMetrics _metrics;

    public PoolMetricsSink(PoolMetrics metrics) => _metrics = metrics;

    public void RecordClaim(string outcome, double latencyMs, DateTime? dispatchedAt = null)
    {
        _metrics.RecordClaim(outcome, latencyMs);
        if (outcome == PoolClaimOutcomes.Success && dispatchedAt.HasValue)
        {
            var wait = DateTime.UtcNow - dispatchedAt.Value;
            _metrics.RecordWait(wait.TotalSeconds);
        }
    }
}
