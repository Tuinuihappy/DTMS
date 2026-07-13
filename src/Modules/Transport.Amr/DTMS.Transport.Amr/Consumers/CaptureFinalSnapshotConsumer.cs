using System.Text.Json;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Amr.Consumers;

/// <summary>
/// When a Trip reaches a terminal state (Completed / Failed / Cancelled),
/// fetch RIOT3's full GET response and persist it on Trip.VendorFinalSnapshot
/// so the detail UI and compliance queries don't need to call out to the
/// vendor. Subscribes to the existing Trip*IntegrationEvents so the
/// capture rides the proven outbox path — no fire-and-forget.
///
/// Idempotent: skips when Trip.VendorFinalSnapshot is already set, so
/// duplicate event delivery and reconciler races are safe.
/// </summary>
public sealed class CaptureFinalSnapshotConsumer :
    IConsumer<TripCompletedIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripCancelledIntegrationEvent>
{
    private readonly ITripRepository _tripRepository;
    private readonly IRiot3OrderQueryService _queryService;
    private readonly ILogger<CaptureFinalSnapshotConsumer> _logger;

    public CaptureFinalSnapshotConsumer(
        ITripRepository tripRepository,
        IRiot3OrderQueryService queryService,
        ILogger<CaptureFinalSnapshotConsumer> logger)
    {
        _tripRepository = tripRepository;
        _queryService = queryService;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TripCompletedIntegrationEvent> context)
        => CaptureForTripAsync(context.Message.TripId, "Completed", context.CancellationToken);

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> context)
        => CaptureForTripAsync(context.Message.TripId, "Failed", context.CancellationToken);

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> context)
        => CaptureForTripAsync(context.Message.TripId, "Cancelled", context.CancellationToken);

    private async Task CaptureForTripAsync(Guid tripId, string terminalState, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(tripId, cancellationToken);
        if (trip is null)
        {
            _logger.LogWarning("[FinalSnapshot] Trip {TripId} not found — cannot capture snapshot.", tripId);
            return;
        }

        // Guard: first writer wins. If the reconciler beat us, skip the
        // vendor fetch entirely.
        if (trip.VendorFinalSnapshot is not null)
        {
            _logger.LogDebug("[FinalSnapshot] Trip {TripId} already has snapshot — skipping.", tripId);
            return;
        }

        if (string.IsNullOrEmpty(trip.UpperKey))
        {
            _logger.LogDebug("[FinalSnapshot] Trip {TripId} has no UpperKey — pre-envelope row, skipping.", tripId);
            return;
        }

        string? raw;
        try
        {
            raw = await _queryService.GetRawByUpperKeyAsync(trip.UpperKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't fail the consumer — reconciler will retry on next tick.
            _logger.LogWarning(ex, "[FinalSnapshot] RIOT3 GET failed for Trip {TripId} (upperKey {UpperKey}); reconciler will retry.",
                tripId, trip.UpperKey);
            return;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("[FinalSnapshot] Vendor returned no record for Trip {TripId} (upperKey {UpperKey}) after {State}.",
                tripId, trip.UpperKey, terminalState);
            return;
        }

        var expectedCompletion = TryExtractFinalTime(raw);
        trip.CaptureFinalSnapshot(raw, expectedCompletion);

        // Backfill the vendor vehicle from the SAME raw we just fetched, before
        // this save seals the trip. Capturing the snapshot flips
        // VendorFinalSnapshot non-null, which drops the trip out of the
        // reconciler's self-heal query (GetTerminalTripsMissingVehicleAsync
        // gates on VendorFinalSnapshot == null) — so if we don't recover the
        // robot here, the self-heal path never gets another chance and the
        // vehicle stays null forever. Fill-only-if-empty + no-op on a null key,
        // so a trip that already captured its robot mid-run is untouched.
        var (vKey, vName) = TryExtractResolvedVehicle(raw);
        var backfilled = trip.BackfillVendorVehicle(vKey, vName, "final-snapshot");

        await _tripRepository.UpdateAsync(trip, cancellationToken);

        _logger.LogInformation(
            "[FinalSnapshot] ✓ Captured for Trip {TripId} (upperKey {UpperKey}, state {State}, size {Bytes}B, vehicle {Vehicle})",
            tripId, trip.UpperKey, terminalState, raw.Length,
            backfilled ? $"backfilled → '{vName ?? vKey}'" : "unchanged");
    }

    /// <summary>
    /// Best-effort extraction of the vendor's finalTime field from the raw
    /// payload. RIOT3 returns ISO 8601; on any error we leave the
    /// expected-completion column null — the raw blob still has the data.
    /// </summary>
    private static DateTime? TryExtractFinalTime(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            if (!data.TryGetProperty("finalTime", out var finalTime)) return null;
            var s = finalTime.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParse(s, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dt) ? dt : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort extraction of the executing robot from the raw payload,
    /// applying the same fallback order as
    /// <see cref="Riot3OrderQueryData.ResolvedVehicle"/> (live processingVehicle,
    /// else the terminal executeVehicle*). Returns (null, null) on any parse
    /// failure — the raw blob still carries the data, so BackfillVendorVehicle
    /// simply no-ops and the snapshot is preserved.
    /// </summary>
    private static (string? Key, string? Name) TryExtractResolvedVehicle(string raw)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<Riot3OrderQueryResponse>(raw);
            return payload?.Data?.ResolvedVehicle ?? (null, null);
        }
        catch
        {
            return (null, null);
        }
    }
}
