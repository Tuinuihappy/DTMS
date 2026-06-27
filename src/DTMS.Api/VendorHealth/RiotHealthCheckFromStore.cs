using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DTMS.Api.VendorHealth;

public sealed class RiotHealthCheckFromStore : IHealthCheck
{
    private const string VendorName = "riot3";

    private readonly IVendorHealthStore _store;

    public RiotHealthCheckFromStore(IVendorHealthStore store)
    {
        _store = store;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _store.Get(VendorName);
        if (snapshot is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "RIOT3 health probe not initialized yet"));
        }

        var description = BuildDescription(snapshot);
        var data = new Dictionary<string, object>
        {
            ["status"] = snapshot.Status.ToString(),
            ["lastCheckedAt"] = snapshot.LastCheckedAt,
            ["lastChangedAt"] = snapshot.LastChangedAt,
            ["consecutiveSuccesses"] = snapshot.ConsecutiveSuccesses,
            ["consecutiveFailures"] = snapshot.ConsecutiveFailures
        };
        if (snapshot.LastOutcome is not null)
        {
            data["latencyMs"] = snapshot.LastOutcome.LatencyMs;
            if (snapshot.LastOutcome.Code is not null)
                data["code"] = snapshot.LastOutcome.Code;
        }

        var result = snapshot.Status switch
        {
            VendorHealthStatus.Healthy => new HealthCheckResult(HealthStatus.Healthy, description, data: data),
            VendorHealthStatus.Degraded => new HealthCheckResult(HealthStatus.Degraded, description, data: data),
            VendorHealthStatus.Unhealthy => new HealthCheckResult(HealthStatus.Unhealthy, description, data: data),
            _ => new HealthCheckResult(HealthStatus.Degraded, description, data: data)
        };
        return Task.FromResult(result);
    }

    private static string BuildDescription(VendorHealthSnapshot snapshot)
    {
        var latency = snapshot.LastOutcome?.LatencyMs;
        return snapshot.Status switch
        {
            VendorHealthStatus.Healthy => $"RIOT3 healthy ({latency}ms, {snapshot.ConsecutiveSuccesses} consecutive successes)",
            VendorHealthStatus.Degraded => snapshot.LastOutcome?.FailureReason
                ?? "RIOT3 degraded",
            VendorHealthStatus.Unhealthy => snapshot.LastOutcome?.FailureReason
                ?? $"RIOT3 unhealthy ({snapshot.ConsecutiveFailures} consecutive failures)",
            _ => "RIOT3 health probe not initialized yet"
        };
    }
}
