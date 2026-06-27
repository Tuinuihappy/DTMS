using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AMR.DeliveryPlanning.Api.Realtime.Drain;

// G1 Phase 1 — readiness flip during pod drain. Tagged "ready" so it's
// part of /health/ready (which K8s service mesh polls to decide routing)
// but NOT /health (which is the liveness probe — kubelet uses it to
// decide whether to restart the container; we don't want a draining pod
// to be RESTARTED, just taken out of rotation).
//
// Reports Unhealthy as soon as ConnectionDrainService.IsDraining is true,
// which causes /health/ready to return 503 and K8s to stop routing new
// traffic. Existing SignalR connections still hold open until the
// settle window broadcasts "__drain" + clients reconnect elsewhere.
internal sealed class DrainHealthCheck : IHealthCheck
{
    private readonly IConnectionDrainService _drain;

    public DrainHealthCheck(IConnectionDrainService drain)
    {
        _drain = drain;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_drain.IsDraining)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"draining since {_drain.StartedAt:o}"));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
