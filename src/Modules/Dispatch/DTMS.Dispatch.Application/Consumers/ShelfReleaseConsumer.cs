using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Facility.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Consumers;

/// <summary>
/// Releases a shelf back to Available when its trip is completed.
/// Flow: Dispatch (Trip Completed) → ShelfManifest lookup → Shelf.SetAvailable()
/// </summary>
public class ShelfReleaseConsumer : IConsumer<TripCompletedIntegrationEvent>
{
    private readonly IShelfManifestRepository _manifestRepo;
    private readonly IShelfRepository _shelfRepo;
    private readonly ILogger<ShelfReleaseConsumer> _logger;

    public ShelfReleaseConsumer(
        IShelfManifestRepository manifestRepo,
        IShelfRepository shelfRepo,
        ILogger<ShelfReleaseConsumer> logger)
    {
        _manifestRepo = manifestRepo;
        _shelfRepo = shelfRepo;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TripCompletedIntegrationEvent> context)
    {
        var evt = context.Message;

        var manifest = await _manifestRepo.GetByTripIdAsync(evt.TripId, context.CancellationToken);
        if (manifest is null)
        {
            // Trip had no shelf — not a LiftUp trip, ignore
            return;
        }

        var shelf = await _shelfRepo.GetByRfidAsync(manifest.ShelfRfid, context.CancellationToken);
        if (shelf is null)
        {
            _logger.LogWarning("[ShelfRelease] Shelf RFID '{Rfid}' not found for Trip {TripId}", manifest.ShelfRfid, evt.TripId);
            return;
        }

        manifest.Release();
        shelf.SetAvailable();

        await _manifestRepo.UpdateAsync(manifest, context.CancellationToken);
        await _shelfRepo.UpdateAsync(shelf, context.CancellationToken);
        await _manifestRepo.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("[ShelfRelease] Shelf '{Rfid}' released after Trip {TripId} completed", manifest.ShelfRfid, evt.TripId);
    }
}
