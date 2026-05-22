using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.ForceStationOffline;

internal sealed class ForceStationOfflineCommandHandler : ICommandHandler<ForceStationOfflineCommand>
{
    private readonly IStationRepository _stationRepository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ForceStationOfflineCommandHandler> _logger;

    public ForceStationOfflineCommandHandler(
        IStationRepository stationRepository,
        IDistributedCache cache,
        ILogger<ForceStationOfflineCommandHandler> logger)
    {
        _stationRepository = stationRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result> Handle(ForceStationOfflineCommand request, CancellationToken cancellationToken)
    {
        var station = await _stationRepository.GetByIdAsync(request.StationId, cancellationToken);
        if (station is null)
            throw new NotFoundException($"Station {request.StationId} not found.");

        try
        {
            station.ForceOffline(
                request.Reason,
                request.By ?? "system",
                TimeSpan.FromMinutes(request.DurationMinutes),
                DateTime.UtcNow);

            await _stationRepository.SaveChangesAsync(cancellationToken);
            await InvalidateLookupCacheAsync(station.Id, station.Code, cancellationToken);

            _logger.LogInformation(
                "[ForceOffline] Station {StationId} forced offline by {By} for {Duration} minutes. Reason: {Reason}.",
                station.Id, request.By ?? "system", request.DurationMinutes, request.Reason);

            return Result.Success();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Removes stale entries from the station lookup cache so that submit-time validation
    /// sees the new override state without waiting for the TTL. Key format mirrors
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
                "[ForceOffline] Cache invalidation failed for {StationId}; falling back to TTL expiry.", stationId);
        }
    }
}
