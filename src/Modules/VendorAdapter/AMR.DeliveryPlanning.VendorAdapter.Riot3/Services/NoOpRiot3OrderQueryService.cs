using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

// Dev/load-test sibling of Riot3OrderQueryService. Returns null for every
// query — Riot3ReconciliationService treats null as "RIOT3 has no record
// of this trip's upperKey" and skips it for the tick (its existing code
// path — see Riot3ReconciliationService.cs:108-114). So with NoOp active,
// the reconciler runs its tick loop normally but generates zero outbound
// HTTP traffic to RIOT3.
//
// Picked at composition time when VendorAdapter:Riot3:Enabled=false —
// same flag that swaps IRobotOrderDispatcher to NoOp. With that flag
// off, ALL outbound RIOT3 traffic (POST orders + GET orders) is
// short-circuited — no real vendor side-effect from any code path the
// load test exercises.
//
// Each call logs at DEBUG (not INF) — the reconciler ticks ~60s and we
// don't want to flood logs in steady state. The DEBUG line still
// surfaces under verbose logging when troubleshooting.
public sealed class NoOpRiot3OrderQueryService : IRiot3OrderQueryService
{
    private readonly ILogger<NoOpRiot3OrderQueryService> _logger;

    public NoOpRiot3OrderQueryService(ILogger<NoOpRiot3OrderQueryService> logger)
    {
        _logger = logger;
    }

    public Task<Riot3OrderQueryData?> GetOrderByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[NoOp] Skipping RIOT3 query for upperKey={UpperKey}", upperKey);
        return Task.FromResult<Riot3OrderQueryData?>(null);
    }

    public Task<string?> GetRawByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[NoOp] Skipping RIOT3 raw query for upperKey={UpperKey}", upperKey);
        return Task.FromResult<string?>(null);
    }
}
