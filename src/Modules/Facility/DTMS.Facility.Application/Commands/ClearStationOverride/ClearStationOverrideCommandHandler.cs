using DTMS.Facility.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DTMS.Facility.Application.Commands.ClearStationOverride;

internal sealed class ClearStationOverrideCommandHandler : ICommandHandler<ClearStationOverrideCommand>
{
    private readonly IStationRepository _stationRepository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ClearStationOverrideCommandHandler> _logger;

    public ClearStationOverrideCommandHandler(
        IStationRepository stationRepository,
        IDistributedCache cache,
        ILogger<ClearStationOverrideCommandHandler> logger)
    {
        _stationRepository = stationRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result> Handle(ClearStationOverrideCommand request, CancellationToken cancellationToken)
    {
        var station = await _stationRepository.GetByIdAsync(request.StationId, cancellationToken);
        if (station is null)
            throw new NotFoundException($"Station {request.StationId} not found.");

        station.ClearManualOverride();
        await _stationRepository.SaveChangesAsync(cancellationToken);
        await InvalidateLookupCacheAsync(station.Id, station.Code, cancellationToken);

        _logger.LogInformation("[ClearOverride] Station {StationId} manual override cleared.", station.Id);
        return Result.Success();
    }

    /// <summary>
    /// Removes stale entries from the station lookup cache so that submit-time validation
    /// sees the cleared override state without waiting for the TTL. Key format mirrors
    /// CachedStationLookup in the DeliveryOrder module — keep these in sync.
    /// </summary>
    private async Task InvalidateLookupCacheAsync(Guid stationId, string? code, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync($"station:lookup:{stationId}", ct);
            if (!string.IsNullOrWhiteSpace(code))
                await _cache.RemoveAsync($"station:lookup:{code}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ClearOverride] Cache invalidation failed for {StationId}; falling back to TTL expiry.", stationId);
        }
    }
}
