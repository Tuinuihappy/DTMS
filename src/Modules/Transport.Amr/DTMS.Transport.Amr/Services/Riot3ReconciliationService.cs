using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.SharedKernel.Diagnostics;
using DTMS.Transport.Amr.Options;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Transport.Amr.UnitTests")]

namespace DTMS.Transport.Amr.Services;

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
    private readonly WorkflowMetrics _metrics;

    public Riot3ReconciliationService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ReconciliationOptions> options,
        ILogger<Riot3ReconciliationService> logger,
        WorkflowMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _metrics = metrics;
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
            // NOTE: filter on the token, NOT the exception type. HttpClient.Timeout
            // surfaces as TaskCanceledException (a subclass of OperationCanceledException)
            // even though stoppingToken was never cancelled — an `is not
            // OperationCanceledException` filter lets that escape ExecuteAsync and the
            // default BackgroundServiceExceptionBehavior.StopHost kills the whole API
            // (crash-loops whenever RIOT3 is slow). Only a genuinely cancelled token
            // (real shutdown) should propagate; everything else is caught + retried.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
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

        if (inFlight.Count == 0)
        {
            // Nothing in the reconcile window this tick. TWO things must still
            // run before bailing:
            //   1. The self-heal backstop targets TERMINAL trips (a webhook
            //      drove completion), independent of in-flight traffic — so an
            //      empty window does NOT mean there's nothing to heal. Skipping
            //      it here meant a trip that completed during a quiet window was
            //      never backfilled until unrelated in-flight traffic reappeared,
            //      and could age out of the SelfHealWindowHours window for good.
            //   2. Refresh trips_stuck; otherwise the gauge goes stale at its
            //      last non-empty-tick value and the alert could miss / false-fire.
            var healedQuiet = await SelfHealMissingVehiclesAsync(tripRepo, queryService, opts, ct);
            var staleOnly = await CountStaleTripsAsync(scope, staleCutoff, ct);
            _metrics.RecordReconcilerTick(tripsStuck: staleOnly, inflight: 0, reconciled: 0, fetchErrors: 0);
            if (healedQuiet > 0)
                _logger.LogInformation("[Reconciler] tick: in-flight=0, self-healed {Healed} terminal trip(s) missing a vehicle", healedQuiet);
            return;
        }

        var reconciled = 0;
        var completed = 0;
        var failed = 0;
        var cancelled = 0;
        var started = 0;
        var paused = 0;
        var resumed = 0;
        var vehicleReassigned = 0;
        var vehicleBackfilled = 0;
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
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
                case Transition.VehicleReassigned:
                    vehicleReassigned++;
                    // Drift = a TASK_PROCESSING reassignment webhook was
                    // dropped and only the reconciler caught it. Surface it so
                    // ops can quantify webhook loss (the root cause).
                    _logger.LogWarning(
                        "[Reconciler] Trip {TripId} (upperKey {UpperKey}) vehicle drift corrected → now '{Vehicle}' (missed reassignment webhook)",
                        trip.Id, trip.UpperKey, data.ProcessingVehicle?.Name ?? data.ProcessingVehicle?.Key ?? "(unknown)");
                    break;
                case Transition.None: break;   // mission upsert may still have run
            }

            if (transition != Transition.None)
            {
                try
                {
                    await tripRepo.UpdateAsync(trip, ct);
                    reconciled++;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
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

                        // Recover the robot from the terminal record for trips
                        // whose TASK_PROCESSING signal was missed — the vehicle
                        // is echoed as executeVehicleKey once finished. No-op
                        // when a live capture already recorded it. Same tick as
                        // the snapshot so both persist in the UpdateAsync below.
                        var (vKey, vName) = data.ResolvedVehicle;
                        if (trip.BackfillVendorVehicle(vKey, vName, "reconciler-terminal"))
                        {
                            vehicleBackfilled++;
                            _logger.LogInformation(
                                "[Reconciler] Trip {TripId} (upperKey {UpperKey}) vehicle backfilled from terminal record → '{Vehicle}' (missed TASK_PROCESSING)",
                                trip.Id, trip.UpperKey, vName ?? vKey ?? "(unknown)");
                        }

                        await tripRepo.UpdateAsync(trip, ct);
                        _logger.LogInformation(
                            "[Reconciler] Captured final snapshot for Trip {TripId} (upperKey {UpperKey}, state {State})",
                            trip.Id, trip.UpperKey, data.State);
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[Reconciler] snapshot capture failed for Trip {TripId} — will retry",
                        trip.Id);
                }
            }
        }

        // Self-heal backstop — terminal trips the in-flight loop never touched
        // because a webhook (not the reconciler) drove the terminal transition,
        // so the terminal snapshot/backfill pass above never ran on them.
        // Bounded + idempotent: each trip drops out once its snapshot is
        // captured here, so this never grows into a per-tick re-fetch loop.
        vehicleBackfilled += await SelfHealMissingVehiclesAsync(tripRepo, queryService, opts, ct);

        var stale = await CountStaleTripsAsync(scope, staleCutoff, ct);
        _logger.LogInformation(
            "[Reconciler] tick: in-flight={InFlight} reconciled={Reconciled} (completed={Completed} failed={Failed} cancelled={Cancelled} started={Started} paused={Paused} resumed={Resumed} vehicleReassigned={VehicleReassigned} vehicleBackfilled={VehicleBackfilled}) noVendor={NoVendor} fetchErr={FetchErr} stale-skipped={Stale}",
            inFlight.Count, reconciled, completed, failed, cancelled, started, paused, resumed, vehicleReassigned, vehicleBackfilled, skippedNoVendorRecord, skippedFetchError, stale);

        // Publish tick outcome to Prometheus (WorkflowMetrics / DTMS.Workflow).
        // trips_stuck (=stale) drives the "AMR order stuck past reconcile window"
        // alert; fetch_error is the leading indicator of RIOT connectivity trouble;
        // backfilled counts post-terminal vehicle recoveries (webhook loss volume).
        _metrics.RecordReconcilerTick(tripsStuck: stale, inflight: inFlight.Count, reconciled: reconciled, fetchErrors: skippedFetchError, backfilled: vehicleBackfilled);
    }

    // internal (not private) so the unit tests can assert the orderState →
    // Transition mapping directly — the SUCCEEDED-vs-FINISHED vocabulary gap
    // (an unrecognized terminal state) is exactly what regressed here.
    internal static async Task<Transition> ApplyVendorStateAsync(
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
                // "FINISHED" is the notify (task.state) success token; the
                // order-level GET (orderState) reports success as "SUCCEEDED".
                // The reconciler reads orderState, so it MUST accept both or it
                // goes blind to every completion whose TASK_FINISHED webhook was
                // lost — the exact failure mode this safety net exists to cover.
                case "FINISHED":
                case "SUCCEEDED":
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
                    if (trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.Created)
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
                    if (trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.Paused)
                    {
                        // Vendor resumed and we missed the HANG/HELD_TO_CONTINUE
                        // webhook — sync back to InProgress so operator commands
                        // map correctly. Also reconcile the robot in case the
                        // resume rode in on a reassignment we didn't see.
                        trip.Resume();
                        trip.ReconcileVehicleAssignment(
                            data.ProcessingVehicle?.Key, data.ProcessingVehicle?.Name, source: "reconciler");
                        return Transition.Resumed;
                    }
                    // Trip already InProgress — backstop a MISSED reassignment
                    // TASK_PROCESSING webhook. RIOT3's order-level
                    // processingVehicle is the current robot; keep DTMS's cache
                    // pointer in sync so operator PASS/CANCEL commands (and the
                    // trip board) target the robot actually running the job.
                    // Idempotent — no-ops (returns false) when the robot is
                    // unchanged, so a steady-state poll produces no transition.
                    if (trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.InProgress
                        && trip.ReconcileVehicleAssignment(
                            data.ProcessingVehicle?.Key, data.ProcessingVehicle?.Name, source: "reconciler"))
                    {
                        return Transition.VehicleReassigned;
                    }
                    return Transition.None;

                case "HANG":
                case "HELD":
                    // Vendor paused — only transition if Trip is currently
                    // InProgress. Created/Paused/terminal states are no-ops.
                    // Capture pause flavour so the resume command picks the
                    // right CONTINUE_FROM_* — see Riot3Webhooks for context.
                    if (trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.InProgress)
                    {
                        var pauseSource = state == "HANG"
                            ? DTMS.Dispatch.Domain.Enums.VendorPauseSource.Hang
                            : DTMS.Dispatch.Domain.Enums.VendorPauseSource.Held;
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

    // Self-heal sweep — see the call site for why it exists. Fetches the
    // authoritative order record for each terminal trip missing a vehicle,
    // captures the snapshot (which permanently drops the trip out of the
    // query), and backfills the robot. Returns the count actually backfilled.
    // internal (not private) so the unit tests can drive one sweep directly
    // without standing up the full IServiceScopeFactory tick harness — every
    // dependency is passed in, so the method is self-contained.
    internal async Task<int> SelfHealMissingVehiclesAsync(
        ITripRepository tripRepo,
        IRiot3OrderQueryService queryService,
        ReconciliationOptions opts,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, opts.SelfHealWindowHours));
        var trips = await tripRepo.GetTerminalTripsMissingVehicleAsync(cutoff, ct);
        if (trips.Count == 0) return 0;

        var healed = 0;
        foreach (var trip in trips)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(trip.UpperKey)) continue;

            try
            {
                var data = await queryService.GetOrderByUpperKeyAsync(trip.UpperKey, ct);
                if (data is null) continue;   // RIOT3 purged the order — retry next tick, bounded by the window

                // TODO(efficiency, low): this hits the SAME RIOT3 endpoint twice
                // (GetOrder parses, GetRaw re-fetches the body). Safe to fold into
                // one round trip because the `data is null` gate above already
                // established code=="0". Bounded (self-heal window + drop-out) so
                // not urgent; revisit only if RIOT3 GET traffic is ever a measured
                // problem. Add a `GetOrderWithRawByUpperKeyAsync` returning
                // (data, raw) — KEEP the standalone raw method (CaptureFinalSnapshot
                // Consumer needs it, and its E110014-only guard differs by design).
                var raw = await queryService.GetRawByUpperKeyAsync(trip.UpperKey, ct);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                // Snapshot FIRST — this is the write that removes the trip from
                // the self-heal query for good, even when no vehicle exists.
                var expectedCompletion = TryParseRiot3Time(data.OrderStateChangeTime ?? data.FinalTime);
                trip.CaptureFinalSnapshot(raw, expectedCompletion);

                var (vKey, vName) = data.ResolvedVehicle;
                var backfilled = trip.BackfillVendorVehicle(vKey, vName, "reconciler-selfheal");
                if (backfilled) healed++;

                await tripRepo.UpdateAsync(trip, ct);
                _logger.LogInformation(
                    "[Reconciler] Self-heal Trip {TripId} (upperKey {UpperKey}): snapshot captured, vehicle {Result}",
                    trip.Id, trip.UpperKey,
                    backfilled ? $"→ '{vName ?? vKey}'" : "unavailable — sealed, no re-fetch");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[Reconciler] Self-heal failed for Trip {TripId} — will retry next tick", trip.Id);
            }
        }
        return healed;
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
        // "SUCCEEDED" is the order-level orderState success token (the notify
        // task.state uses "FINISHED"); both must gate snapshot + vehicle backfill.
        return s is "FINISHED" or "SUCCEEDED" or "FAILED" or "CANCELED" or "CANCELLED" or "REJECTED";
    }

    private static DateTime? TryParseRiot3Time(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt) ? dt : null;
    }

    internal enum Transition { None, Completed, Failed, Cancelled, Started, Paused, Resumed, VehicleReassigned }
}
