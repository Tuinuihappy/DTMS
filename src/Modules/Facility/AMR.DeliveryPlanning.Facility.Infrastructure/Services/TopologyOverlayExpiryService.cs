using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

public class TopologyOverlayExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TopologyOverlayExpiryService> _logger;

    public TopologyOverlayExpiryService(IServiceScopeFactory scopeFactory, ILogger<TopologyOverlayExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessExpiredOverlaysAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ProcessExpiredOverlaysAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITopologyOverlayRepository>();

        var expired = await repo.GetExpiredAsync(cancellationToken);
        if (expired.Count == 0) return;

        _logger.LogInformation("Found {Count} expired topology overlays to clean up", expired.Count);

        foreach (var overlay in expired)
        {
            _logger.LogDebug("Topology overlay {Id} ({Type}) on map {MapId} expired at {ExpiredAt}",
                overlay.Id, overlay.Type, overlay.MapId, overlay.ValidUntil);
        }
    }
}
