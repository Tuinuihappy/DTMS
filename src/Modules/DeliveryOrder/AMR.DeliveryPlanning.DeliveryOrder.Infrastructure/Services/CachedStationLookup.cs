using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;

public class CachedStationLookup : IStationLookup
{
    private readonly FacilityStationLookup _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedStationLookup> _logger;

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
    };

    public CachedStationLookup(
        FacilityStationLookup inner,
        IDistributedCache cache,
        ILogger<CachedStationLookup> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public Task<bool> ExistsAsync(Guid stationId, CancellationToken ct = default)
        => _inner.ExistsAsync(stationId, ct);

    public Task<Guid?> ResolveByCodeAsync(string code, CancellationToken ct = default)
        => _inner.ResolveByCodeAsync(code, ct);

    public async Task<IReadOnlyDictionary<string, Guid>> ResolveBatchAsync(
        IReadOnlyList<string> locationCodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var uncached = new List<string>();

        foreach (var code in locationCodes)
        {
            var cached = await _cache.GetStringAsync($"station:code:{code}", ct);
            if (cached != null && Guid.TryParse(cached, out var stationId))
            {
                result[code] = stationId;
                _logger.LogDebug("Cache hit for station code {Code}", code);
            }
            else
            {
                uncached.Add(code);
            }
        }

        if (uncached.Count == 0)
            return result;

        var resolved = await _inner.ResolveBatchAsync(uncached, ct);

        foreach (var (code, id) in resolved)
        {
            result[code] = id;
            await _cache.SetStringAsync($"station:code:{code}", id.ToString(), CacheOptions, ct);
        }

        return result;
    }
}
