using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Services;
using AMR.DeliveryPlanning.VendorAdapter.Feeder.Options;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.VendorAdapter.Feeder.Services;

/// <summary>
/// Polls RIOT3 for envelope-dispatched trips that haven't reached a
/// terminal state, then reconciles vendor state back into the Trip
/// aggregate. Safety net for dropped/missed webhook callbacks — webhooks
/// remain the primary signal; this service is the backstop.
///
/// Idempotency: every Trip.MarkVendor* method is a no-op when the trip is
/// already in the target state, so racing with a webhook does no harm.
/// </summary>
public sealed class Riot3ReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ReconciliationOptions> _options;
    private readonly ILogger<Riot3ReconciliationService> _logger;

    public Riot3ReconciliationService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ReconciliationOptions> options,
        ILogger<Riot3ReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Riot3ReconciliationService started (enabled={Enabled}, interval={Interval}s, stale>{Stale}h)",
            _options.CurrentValue.Enabled,
            _options.CurrentValue.PollIntervalSeconds,
            _options.CurrentValue.StaleThresholdHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;
            try
            {
                if (opts.Enabled)
                    await ReconcileTickAsync(opts, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[Reconciler] tick failed unexpectedly");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, opts.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ReconcileTickAsync(ReconciliationOptions opts, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tripRepo = scope.ServiceProvider.GetRequiredService<ITripRepository>();
        var missionRepo = scope.ServiceProvider.GetRequiredService<ITripMissionEventRepository>();
        var queryService = scope.ServiceProvider.GetRequiredService<IRiot3OrderQueryService>();
        var realtimePublisher = scope.ServiceProvider.GetRequiredService<ITripRealtimePublisher>();
        // Phase P5.3 — used when reconciler observes PROCESSING for a trip
        // that's still Created (we missed the TASK_PROCESSING webhook).
        var itemSnapshotProvider = scope.ServiceProvider.GetRequiredService<ITripItemSnapshotProvider>();

        var staleCutoff = DateTime.UtcNow.AddHours(-opts.StaleThresholdHours);
        var inFlight = await tripRepo.GetInFlightEnvelopeTripsAsync(staleCutoff, ct);

        if (inFlight.Count == 0) return;

        var reconciled = 0;
        var completed = 0;
        var failed = 0;
        var cancelled = 0;
        var started = 0;
        var paused = 0;
        var resumed = 0;
        var skippedNoVendorRecord = 0;
        var skippedFetchError = 0;

        foreach (var trip in inFlight)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(trip.UpperKey)) continue;

            Riot3OrderQueryData? data;
            try
            {
                data = await queryService.GetOrderByUpperKeyAsync(trip.UpperKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skippedFetchError++;
                _logger.LogWarning(ex, "[Reconciler] fetch failed for Trip {TripId} (upperKey {UpperKey}) — will retry next tick",
                    trip.Id, trip.UpperKey);
                continue;
            }

            if (data is null)
            {
                skippedNoVendorRecord++;
                _logger.LogDebug("[Reconciler] Trip {TripId} (upperKey {UpperKey}) — RIOT3 has no record yet (just dispatched?)",
                    trip.Id, trip.UpperKey);
                continue;
            }

            // Mission diff — independent of state transition. Even when
            // Trip status didn't change this tick, sub-task progress may
            // have arrived; upsert is idempotent so duplicates are safe.
            await UpsertMissionsAsync(missionRepo, realtimePublisher, trip.Id, data, ct);

            var transition = await ApplyVendorStateAsync(trip, data, itemSnapshotProvider, ct);
            switch (transition)
            {
                case Transition.Completed: completed++; break;
                case Transition.Failed: failed++; break;
                case Transition.Cancelled: cancelled++; break;
                case Transition.Started: started++; break;
                case Transition.Paused: paused++; break;
                case Transition.Resumed: resumed++; break;
                case Transition.None: break;   // mission upsert may still have run
            }

            if (transition != Transition.None)
            {
                try
                {
                    await tripRepo.UpdateAsync(trip, ct);
                    reconciled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[Reconciler] persist failed for Trip {TripId} (upperKey {UpperKey})",
                        trip.Id, trip.UpperKey);
                    continue;
                }
            }

            // Final snapshot capture — safety net for the webhook consumer.
            // Only fetch if we don't already have one AND the vendor state
            // is terminal. The Trip.CaptureFinalSnapshot guard ensures
            // first-write-wins so the webhook consumer and us are race-safe.
            if (trip.VendorFinalSnapshot is null && IsTerminalVendorState(data.State))
            {
                try
                {
                    var raw = await queryService.GetRawByUpperKeyAsync(trip.UpperKey!, ct);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var expectedCompletion = TryParseRiot3Time(data.OrderStateChangeTime ?? data.FinalTime);
                        trip.CaptureFinalSnapshot(raw, expectedCompletion);
                        await tripRepo.UpdateAsync(trip, ct);
                        _logger.LogInformation(
                            "[Reconciler] Captured final snapshot for Trip {TripId} (upperKey {UpperKey}, state {State})",
                            trip.Id, trip.UpperKey, data.State);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "[Reconciler] snapshot capture failed for Trip {TripId} — will retry",
                        trip.Id);
                }
            }
        }

        var stale = await CountStaleTripsAsync(scope, staleCutoff, ct);
        _logger.LogInformation(
            "[Reconciler] tick: in-flight={InFlight} reconciled={Reconciled} (completed={Completed} failed={Failed} cancelled={Cancelled} started={Started} paused={Paused} resumed={Resumed}) noVendor={NoVendor} fetchErr={FetchErr} stale-skipped={Stale}",
            inFlight.Count, reconciled, completed, failed, cancelled, started, paused, resumed, skippedNoVendorRecord, skippedFetchError, stale);
    }

    private static async Task<Transition> ApplyVendorStateAsync(
        Trip trip,
        Riot3OrderQueryData data,
        ITripItemSnapshotProvider itemSnapshotProvider,
        CancellationToken ct)
    {
        var state = data.State?.ToUpperInvariant();
        try
        {
            switch (state)
            {
                case "FINISHED":
                    trip.MarkVendorCompleted();
                    return Transition.Completed;

                case "FAILED":
                    var failReason = data.FailReason?.ErrorDescription
                                     ?? data.FailReason?.ErrorCode
                                     ?? "vendor reported failure";
                    trip.MarkVendorFailed(failReason);
                    return Transition.Failed;

                case "CANCELED":
                case "CANCELLED":
                    // Vendor cancel mirrors operator cancel — Trip.Cancel
                    // moves to Cancelled and DeliveryOrder is left alone so
                    // the order can be re-dispatched. (TASK_FAILED below
                    // remains the only path that fails the DeliveryOrder.)
                    trip.Cancel(data.CancelReason ?? "vendor cancelled");
                    return Transition.Cancelled;

                case "PROCESSING":
                    if (trip.Status == AMR.DeliveryPlanning.Dispatch.Domain.Enums.TripStatus.Created)
                    {
                        // Same as the webhook: capture the vendor deviceKey
                        // string as-is. No Fleet resolver call here.
                        // Phase P5.3 — snapshot items for TripItemsProjector.
                        var itemSnapshots = await itemSnapshotProvider.GetForTripAsync(trip.Id, ct);
                        trip.MarkVendorStarted(
                            vehicleId: null,
                            vendorVehicleKey: data.ProcessingVehicle?.Key,
                            vendorVehicleName: data.ProcessingVehicle?.Name,
                            items: itemSnapshots);
                        return Transition.Started;
                    }
                    if (trip.Status == AMR.DeliveryPlanning.Dispatch.Domain.Enums.TripStatus.Paused)
                    {
                        // Vendor resumed and we missed the HANG/HELD_TO_CONTINUE
                        // webhook — sync back to InProgress so operator commands
                        // map correctly.
                        trip.Resume();
                        return Transition.Resumed;
                    }
                    return Transition.None;

                case "HANG":
                case "HELD":
                    // Vendor paused — only transition if Trip is currently
                    // InProgress. Created/Paused/terminal states are no-ops.
                    // Capture pause flavour so the resume command picks the
                    // right CONTINUE_FROM_* — see Riot3Webhooks for context.
                    if (trip.Status == AMR.DeliveryPlanning.Dispatch.Domain.Enums.TripStatus.InProgress)
                    {
                        var pauseSource = state == "HANG"
                            ? AMR.DeliveryPlanning.Dispatch.Domain.Enums.VendorPauseSource.Hang
                            : AMR.DeliveryPlanning.Dispatch.Domain.Enums.VendorPauseSource.Held;
                        trip.Pause(pauseSource);
                        return Transition.Paused;
                    }
                    return Transition.None;

                case "REJECTED":
                    // Vendor refused the task post-dispatch. Treat as failure
                    // so the DeliveryOrder propagates to Failed.
                    var rejectReason = data.FailReason?.ErrorDescription
                                       ?? data.FailReason?.ErrorCode
                                       ?? "vendor rejected task";
                    trip.MarkVendorFailed(rejectReason);
                    return Transition.Failed;

                default:
                    return Transition.None;
            }
        }
        catch (InvalidOperationException)
        {
            // Trip already in a terminal state that conflicts — webhook
            // likely landed between query and apply. Safe to ignore.
            return Transition.None;
        }
    }

    private static async Task<int> CountStaleTripsAsync(IServiceScope scope, DateTime cutoff, CancellationToken ct)
    {
        var tripRepo = scope.ServiceProvider.GetRequiredService<ITripRepository>();
        // We compute "stale" as in-flight envelope trips OLDER than the cutoff.
        // The repository's in-flight query already excludes them — so to count
        // staleness we re-query with a very old floor and subtract. Cheap enough
        // for ops visibility; if perf ever matters we'll add a dedicated count.
        var allEver = await tripRepo.GetInFlightEnvelopeTripsAsync(DateTime.MinValue, ct);
        return allEver.Count(t => t.CreatedAt < cutoff);
    }

    // ── Mission diff + final snapshot helpers ────────────────────────────

    private static async Task UpsertMissionsAsync(
        ITripMissionEventRepository repo,
        ITripRealtimePublisher realtimePublisher,
        Guid tripId,
        Riot3OrderQueryData data,
        CancellationToken ct)
    {
        if (data.Missions is null || data.Missions.Count == 0) return;

        for (var i = 0; i < data.Missions.Count; i++)
        {
            var m = data.Missions[i];
            if (string.IsNullOrWhiteSpace(m.MissionKey) || string.IsNullOrWhiteSpace(m.State))
                continue;

            var state = m.State!.ToUpperInvariant();
            if (state is "NA" or "QUEUEING")
                // Mission not yet picked up by vendor; nothing useful to record.
                continue;

            try
            {
                var ev = TripMissionEvent.Record(
                    tripId: tripId,
                    missionIndex: m.MissionIndex ?? i,
                    missionKey: m.MissionKey!,
                    missionType: string.IsNullOrWhiteSpace(m.Type) ? "UNKNOWN" : m.Type!,
                    state: state,
                    changeStateTime: TryParseRiot3Time(m.ChangeStateTime) ?? DateTime.UtcNow,
                    stationName: m.StationName,
                    actionName: m.ActionName,
                    actionType: m.ActionType,
                    resultCode: m.ResultCode,
                    errorMessage: m.ResultStr);
                var inserted = await repo.AddIfNotExistsAsync(ev, ct);
                if (inserted)
                {
                    // Mirror webhook behavior: push to the operator drawer
                    // so a missed webhook surfaces in realtime once the
                    // reconciler catches up. Publisher swallows transport
                    // errors so a SignalR hiccup never aborts the tick.
                    await realtimePublisher.PublishMissionUpdatedAsync(
                        tripId,
                        new TripMissionEventDto(
                            MissionIndex: ev.MissionIndex,
                            MissionKey: ev.MissionKey,
                            MissionType: ev.MissionType,
                            State: ev.State,
                            StationName: ev.StationName,
                            ActionName: ev.ActionName,
                            ActionType: ev.ActionType,
                            ResultCode: ev.ResultCode,
                            ErrorMessage: ev.ErrorMessage,
                            ChangeStateTime: ev.ChangeStateTime,
                            ReceivedAt: ev.ReceivedAt),
                        ct);
                }
            }
            catch (ArgumentException)
            {
                // Defensive: vendor sent a malformed mission record. Skip
                // this one rather than abort the whole tick.
            }
        }
    }

    private static bool IsTerminalVendorState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        var s = state.ToUpperInvariant();
        return s is "FINISHED" or "FAILED" or "CANCELED" or "CANCELLED" or "REJECTED";
    }

    private static DateTime? TryParseRiot3Time(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt) ? dt : null;
    }

    private enum Transition { None, Completed, Failed, Cancelled, Started, Paused, Resumed }
}
