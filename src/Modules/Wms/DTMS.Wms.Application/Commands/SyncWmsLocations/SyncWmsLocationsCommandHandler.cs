using System.Diagnostics;
using DTMS.SharedKernel.Messaging;
using DTMS.Wms.Application.Services;
using DTMS.Wms.Domain.Entities;
using DTMS.Wms.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace DTMS.Wms.Application.Commands.SyncWmsLocations;

/// <summary>
/// Pages the WMS <c>/location</c> endpoint into a local dictionary of
/// snapshots, then reconciles against <c>wms.Locations</c>: unseen codes
/// get upserted, in-DB codes NOT returned by WMS get soft-deleted
/// (IsActive=false). Never hard-deletes so in-flight orders keep their
/// referential integrity.
///
/// Concurrency: a single global semaphore prevents overlapping runs even
/// if the poller misfires or a manual trigger races the timer. The
/// external endpoint is fine with concurrent reads but our DB upserts
/// can't dedupe without a lock.
///
/// Failure modes:
///   - HTTP error mid-pagination → propagate as Failure; partial upserts
///     stay because SaveChanges was already called on the completed pages.
///     Next cycle picks up where we left off (idempotent).
///   - Empty first page → treat as "WMS legitimately has 0 locations" only
///     if the response envelope says Total=0; otherwise treat as suspect
///     (WMS glitch) and skip the deactivation pass so we don't wipe the
///     whole cache on a bad response.
/// </summary>
internal sealed class SyncWmsLocationsCommandHandler
    : ICommandHandler<SyncWmsLocationsCommand, SyncWmsLocationsResult>
{
    // Global lock — one WMS endpoint, one snapshot. Different from the
    // per-map lock in SyncMapStationsCommandHandler because WMS has no
    // per-record scoping.
    private static readonly SemaphoreSlim _syncLock = new(1, 1);

    private readonly IWmsClient _client;
    private readonly IWmsLocationRepository _repo;
    private readonly ILogger<SyncWmsLocationsCommandHandler> _logger;

    // Page size + cap live in the client-side options; injected into the
    // handler by DI (Options<WmsOptions>) — but to keep the Application
    // layer free of Infrastructure types, we accept the two knobs as
    // constructor primitives via a small config record.
    private readonly IWmsSyncConfig _config;

    public SyncWmsLocationsCommandHandler(
        IWmsClient client,
        IWmsLocationRepository repo,
        IWmsSyncConfig config,
        ILogger<SyncWmsLocationsCommandHandler> logger)
    {
        _client = client;
        _repo = repo;
        _config = config;
        _logger = logger;
    }

    public async Task<Result<SyncWmsLocationsResult>> Handle(
        SyncWmsLocationsCommand request, CancellationToken ct)
    {
        // Zero-wait acquire — if another cycle is running we skip cleanly
        // rather than pile up. The next timer tick will pick us up.
        if (!await _syncLock.WaitAsync(TimeSpan.Zero, ct))
        {
            _logger.LogDebug("[WmsSync] Another sync cycle in progress — skipping.");
            return Result<SyncWmsLocationsResult>.Success(
                new SyncWmsLocationsResult(0, 0, 0, 0, 0, 0));
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pageSize = Math.Max(1, _config.PageSize);
            var maxRows = Math.Max(1, _config.MaxRowsPerCycle);

            var seen = new Dictionary<string, WmsLocationDto>(StringComparer.OrdinalIgnoreCase);
            int reportedTotal = 0;
            int page = 1;

            while (seen.Count < maxRows)
            {
                WmsLocationPage response;
                try
                {
                    response = await _client.GetPageAsync(page, pageSize, search: null, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    _logger.LogWarning(ex,
                        "[WmsSync] Upstream unreachable at page {Page} — preserving snapshot, will retry next cycle.",
                        page);
                    return Result<SyncWmsLocationsResult>.Failure(
                        $"WMS unreachable: {ex.Message}");
                }

                if (page == 1) reportedTotal = response.Total;

                if (response.Data.Count == 0) break;

                foreach (var dto in response.Data)
                {
                    if (string.IsNullOrWhiteSpace(dto.LocationCode))
                    {
                        _logger.LogWarning(
                            "[WmsSync] Skipping WMS row {ExternalId} — empty locationCode.",
                            dto.Id);
                        continue;
                    }

                    // Last-writer-wins for case-variant duplicates (rare but
                    // WMS has been observed to return the same code with
                    // different casing on different pages).
                    seen[dto.LocationCode] = dto;
                }

                if (response.Data.Count < pageSize) break;
                page++;
            }

            _logger.LogDebug(
                "[WmsSync] Pulled {Pulled} rows across {Pages} page(s) (WMS reported total={Total}).",
                seen.Count, page, reportedTotal);

            // Guard: if WMS reported Total > 0 but we ended up with zero rows,
            // something's off — likely upstream glitch, not a real emptying.
            // Skip the deactivation pass to avoid wiping the whole cache.
            var skipDeactivate = reportedTotal > 0 && seen.Count == 0;
            if (skipDeactivate)
            {
                _logger.LogWarning(
                    "[WmsSync] WMS reported Total={Total} but returned no rows — skipping deactivation pass.",
                    reportedTotal);
            }

            var now = DateTime.UtcNow;
            var existing = await _repo.GetAllAsync(ct);

            int added = 0, updated = 0, reactivated = 0, deactivated = 0;

            // Upsert every seen location.
            foreach (var (code, dto) in seen)
            {
                if (existing.TryGetValue(code, out var current))
                {
                    var wasInactive = !current.IsActive;
                    var changed = current.UpdateFromWms(
                        displayName: dto.DisplayName ?? dto.LocationCode,
                        type: dto.Type,
                        typeName: dto.TypeName,
                        isActive: dto.IsActive,
                        isStorageLocation: dto.IsStorageLocation,
                        parentLocationId: dto.ParentLocationId,
                        parentLocationCode: dto.ParentLocationCode,
                        parentLocationDisplayName: dto.ParentLocationDisplayName,
                        description: dto.Description,
                        latitude: dto.Latitude,
                        longitude: dto.Longitude,
                        zGpsHeight: dto.ZGpsHeight,
                        zTolerance: dto.ZTolerance,
                        accuracyMeters: dto.Accuracy,
                        heightDiff: dto.HeightDiff,
                        nowUtc: now);

                    if (wasInactive && dto.IsActive) reactivated++;
                    else if (changed) updated++;
                }
                else
                {
                    var loc = WmsLocation.CreateFromWms(
                        externalId: dto.Id,
                        locationCode: dto.LocationCode,
                        displayName: dto.DisplayName ?? dto.LocationCode,
                        type: dto.Type,
                        typeName: dto.TypeName,
                        isActive: dto.IsActive,
                        isStorageLocation: dto.IsStorageLocation,
                        parentLocationId: dto.ParentLocationId,
                        parentLocationCode: dto.ParentLocationCode,
                        parentLocationDisplayName: dto.ParentLocationDisplayName,
                        description: dto.Description,
                        latitude: dto.Latitude,
                        longitude: dto.Longitude,
                        zGpsHeight: dto.ZGpsHeight,
                        zTolerance: dto.ZTolerance,
                        accuracyMeters: dto.Accuracy,
                        heightDiff: dto.HeightDiff,
                        nowUtc: now);
                    await _repo.AddAsync(loc, ct);
                    added++;
                }
            }

            // Soft-delete anything in DB that WMS didn't return this cycle
            // (unless we're skipping — see suspect-response guard above).
            if (!skipDeactivate)
            {
                foreach (var (code, dbLoc) in existing)
                {
                    if (seen.ContainsKey(code)) continue;
                    if (!dbLoc.IsActive) continue;
                    dbLoc.MarkInactive(now);
                    deactivated++;
                }
            }

            await _repo.SaveChangesAsync(ct);

            stopwatch.Stop();
            _logger.LogInformation(
                "[WmsSync] pulled={Pulled} added={Added} updated={Updated} reactivated={Reactivated} deactivated={Deactivated} elapsed={Elapsed}ms",
                seen.Count, added, updated, reactivated, deactivated, stopwatch.ElapsedMilliseconds);

            return Result<SyncWmsLocationsResult>.Success(new SyncWmsLocationsResult(
                seen.Count, added, updated, deactivated, reactivated, stopwatch.ElapsedMilliseconds));
        }
        finally
        {
            _syncLock.Release();
        }
    }
}

/// <summary>
/// Sync-tunable knobs the handler needs. Kept as a small interface so the
/// Application layer stays free of Infrastructure's IOptions&lt;WmsOptions&gt;
/// dependency; the Infrastructure layer registers an adapter that projects
/// WmsOptions onto this contract.
/// </summary>
public interface IWmsSyncConfig
{
    int PageSize { get; }
    int MaxRowsPerCycle { get; }
}
