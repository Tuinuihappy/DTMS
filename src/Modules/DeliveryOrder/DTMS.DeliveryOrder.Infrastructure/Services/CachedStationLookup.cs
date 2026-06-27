using System.Text.Json;
using DTMS.DeliveryOrder.Application.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Infrastructure.Services;

public class CachedStationLookup : IStationLookup
{
    private readonly FacilityStationLookup _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedStationLookup> _logger;

    // Short TTL so deactivation / manual override propagates within ~1 min.
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
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

    public async Task<IReadOnlyDictionary<string, StationLookupResult>> ResolveBatchAsync(
        IReadOnlyList<string> locationCodes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, StationLookupResult>(StringComparer.OrdinalIgnoreCase);
        var uncached = new List<string>();

        foreach (var code in locationCodes)
        {
            var cached = await _cache.GetStringAsync($"station:lookup:{code}", ct);
            if (cached != null)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<StationLookupResult>(cached);
                    if (entry != null)
                    {
                        result[code] = entry;
                        _logger.LogDebug("Cache hit for station {Code}", code);
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // stale cache shape from a previous deploy — fall through and re-resolve
                }
            }
            uncached.Add(code);
        }

        if (uncached.Count == 0)
            return result;

        var resolved = await _inner.ResolveBatchAsync(uncached, ct);

        foreach (var (code, entry) in resolved)
        {
            result[code] = entry;
            await _cache.SetStringAsync(
                $"station:lookup:{code}",
                JsonSerializer.Serialize(entry),
                CacheOptions, ct);
        }

        return result;
    }
}
