using System.Text;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class CachedRouteCostCalculator : IRouteCostCalculator
{
    private readonly IRouteCostCalculator _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedRouteCostCalculator> _logger;
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15)
    };

    public CachedRouteCostCalculator(
        SimpleRouteCostCalculator inner,
        IDistributedCache cache,
        ILogger<CachedRouteCostCalculator> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<double> CalculateCostAsync(Guid fromStationId, Guid toStationId, CancellationToken cancellationToken = default)
    {
        var key = $"route:{fromStationId}:{toStationId}";

        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (cached != null && double.TryParse(cached, out var cachedCost))
        {
            _logger.LogDebug("Cache hit for route {From}→{To}", fromStationId, toStationId);
            return cachedCost;
        }

        var cost = await _inner.CalculateCostAsync(fromStationId, toStationId, cancellationToken);

        await _cache.SetStringAsync(key, cost.ToString("R"), CacheOptions, cancellationToken);
        return cost;
    }

    public double Calculate(Guid fromStationId, Guid toStationId) =>
        _inner.Calculate(fromStationId, toStationId);
}
