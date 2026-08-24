using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.SharedKernel.Diagnostics;
using DTMS.Transport.Amr.Options;
using DTMS.Transport.Amr.Models;
using DTMS.Transport.Amr.Services;
using Microsoft.EntityFrameworkCore;
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
        _logger.LogInformation("Riot3ReconciliationService started (enabled={Enabled}, interval={Interval}s, stuck-alert>{Stale}h)",
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
        // Pickup/drop detection safety net — the reconciler runs the SAME
        // station-match logic as the webhook so a dropped sub-task webhook
        // doesn't lose the pickup/drop signal (or fire it on the wrong visit).
        var facilityReadService = scope.ServiceProvider.GetRequiredService<DTMS.Facility.Application.Services.IFacilityReadService>();
        var orderReader = scope.ServiceProvider.GetRequiredService<DTMS.Dispatch.Application.Services.IDeliveryOrderStatusReader>();

        // EVERY non-terminal envelope trip is polled, regardless of age — a
        // vendor-side terminal transition that lands late (e.g. an operator
        // cancels a long-HANG order in the RIOT3 console days after dispatch)
        // plus a dropped webhook used to wedge the trip forever once it aged
        // past the old skip window. StaleThresholdHours now only classifies:
        //   - stale → trips_stuck gauge (the "needs a human look" alert)
        //   - fresh → inflight gauge, which gates the notify-silence alert;
        //     counting a long-stuck HANG trip there would hold the gate open
        //     while it legitimately produces no frames (false P1).
        var staleCutoff = DateTime.UtcNow.AddHours(-opts.StaleThresholdHours);
        var inFlight = await tripRepo.GetInFlightEnvelopeTripsAsync(ct);
        var stale = inFlight.Count(t => t.CreatedAt < staleCutoff);
        var fresh = inFlight.Count - stale;

        if (inFlight.Count == 0)
        {
            // Nothing in flight this tick. The self-heal backstop must still
            // run before bailing: it targets TERMINAL trips (a webhook drove
            // completion), independent of in-flight traffic — so an empty
            // list does NOT mean there's nothing to heal. Skipping it here
            // meant a trip that completed during a quiet window was never
            // backfilled until unrelated in-flight traffic reappeared, and
            // could age out of the SelfHealWindowHours window for good.
            // trips_stuck is also refreshed (trivially 0) so the gauge never
            // goes stale at its last non-empty-tick value.
            var healedQuiet = await SelfHealMissingVehiclesAsync(tripRepo, queryService, opts, ct);
            _metrics.RecordReconcilerTick(tripsStuck: 0, inflight: 0, reconciled: 0, fetchErrors: 0);
            if (healedQuiet > 0)
                _logger.LogInformation("[Reconciler] tick: in-flight=0, self-healed {Healed} terminal trip(s) missing a vehicle", healedQuiet);
            return;
        }

        var reconciled = 0;
        var completed = 0;
        var failed = 0;
        var rejected = 0;
        var cancelled = 0;
        var started = 0;
        var hang = 0;
        var held = 0;
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

            // Pickup/drop detection safety net — fire-once at the Trip means a
            // signal the webhook already recorded is a no-op here; this only
            // catches the ones the webhook dropped.
            var stationFired = await DetectStationTransitionsAsync(
                trip, data, facilityReadService, orderReader, ct);

            var transition = await ApplyVendorStateAsync(trip, data, itemSnapshotProvider, ct);
            switch (transition)
            {
                case Transition.Completed: completed++; break;
                case Transition.Failed: failed++; break;
                case Transition.Rejected: rejected++; break;
                case Transition.Cancelled: cancelled++; break;
                case Transition.Started: started++; break;
                case Transition.Hang: hang++; break;
                case Transition.Held: held++; break;
                case Transition.Resumed: resumed++; break;
                case Transition.VehicleReassigned:
                    vehicleReassigned++;
                    // Drift = a TASK_PROCESSING reassignment webhook was
                    // dropped and only the reconciler caught it. Surface it so
                    // ops can quantify webhook loss (the root cause).
                    _logger.LogWarning(
                        "[Reconciler] Trip {TripId} (upperKey {UpperKey}) vehicle drift corrected → now '{Vehicle}' (missed reassignment webhook)",
                        trip.Id, trip.UpperKey, data.ResolvedVehicle.Name ?? data.ResolvedVehicle.Key ?? "(unknown)");
                    break;
                case Transition.None: break;   // mission upsert may still have run
            }

            // Persist when the vendor state transitioned OR a pickup/drop signal
            // fired (the latter mutates the Trip without a status transition).
            if (transition != Transition.None || stationFired)
            {
                try
                {
                    await tripRepo.UpdateAsync(trip, ct);
                    if (transition != Transition.None) reconciled++;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // A webhook won the race for this trip. The whole batch
                    // shares one DbContext, and the failed save leaves the
                    // conflicted trip Modified + its drained domain events
                    // queued as Added OutboxMessages — the NEXT trip's save
                    // would re-throw and/or insert those duplicate events.
                    // Abort the tick instead of continuing; the next tick
                    // (≤60s) reloads everything fresh. MUST precede the
                    // generic catch below.
                    AbortTickOnConflict(trip, stale, fresh,
                        reconciled, skippedFetchError, vehicleBackfilled);
                    return;
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
                        var expectedCompletion = Riot3MissionEventFactory.ParseRiot3Time(data.OrderStateChangeTime ?? data.FinalTime);
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
                catch (DbUpdateConcurrencyException)
                {
                    // Same poisoned-batch reasoning as the transition save
                    // above (likely CaptureFinalSnapshotConsumer racing us).
                    // MUST precede the generic catch below.
                    AbortTickOnConflict(trip, stale, fresh,
                        reconciled, skippedFetchError, vehicleBackfilled);
                    return;
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

        _logger.LogInformation(
            "[Reconciler] tick: in-flight={InFlight} (fresh={Fresh} stale={Stale}) reconciled={Reconciled} (completed={Completed} failed={Failed} rejected={Rejected} cancelled={Cancelled} started={Started} hang={Hang} held={Held} resumed={Resumed} vehicleReassigned={VehicleReassigned} vehicleBackfilled={VehicleBackfilled}) noVendor={NoVendor} fetchErr={FetchErr}",
            inFlight.Count, fresh, stale, reconciled, completed, failed, rejected, cancelled, started, hang, held, resumed, vehicleReassigned, vehicleBackfilled, skippedNoVendorRecord, skippedFetchError);

        // Publish tick outcome to Prometheus (WorkflowMetrics / DTMS.Workflow).
        // trips_stuck (=stale) drives the "AMR order stuck past reconcile window"
        // alert — such trips ARE still polled, the alert just asks a human to
        // find out why they've been non-terminal this long. inflight counts
        // fresh trips only (gates the notify-silence alert). fetch_error is the
        // leading indicator of RIOT connectivity trouble; backfilled counts
        // post-terminal vehicle recoveries (webhook loss volume).
        _metrics.RecordReconcilerTick(tripsStuck: stale, inflight: fresh, reconciled: reconciled, fetchErrors: skippedFetchError, backfilled: vehicleBackfilled);
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
                    // Robot identity: prefer the live processingVehicle, but fall
                    // back to executeVehicleKey/Name. Some RIOT3 deployments never
                    // populate processingVehicle at the order level (nor emit a
                    // task-level TASK_PROCESSING webhook) — they only ever report
                    // the executing robot under executeVehicle*. Reading
                    // ProcessingVehicle directly here left the vehicle null for
                    // the entire run on those vendors; ResolvedVehicle covers both.
                    var (vehKey, vehName) = data.ResolvedVehicle;
                    if (trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.Created)
                    {
                        // Same as the webhook: capture the vendor deviceKey
                        // string as-is. No Fleet resolver call here.
                        // Phase P5.3 — snapshot items for TripItemsProjector.
                        var itemSnapshots = await itemSnapshotProvider.GetForTripAsync(trip.Id, ct);
                        trip.MarkVendorStarted(
                            vehicleId: null,
                            vendorVehicleKey: vehKey,
                            vendorVehicleName: vehName,
                            items: itemSnapshots);
                        return Transition.Started;
                    }
                    if (trip.Status is DTMS.Dispatch.Domain.Enums.TripStatus.Hang
                                     or DTMS.Dispatch.Domain.Enums.TripStatus.Held)
                    {
                        // Vendor resumed and we missed the HANG/HELD_TO_CONTINUE
                        // webhook — sync back to InProgress so operator commands
                        // map correctly. Also reconcile the robot in case the
                        // resume rode in on a reassignment we didn't see.
                        trip.Resume();
                        trip.ReconcileVehicleAssignment(vehKey, vehName, source: "reconciler");
                        return Transition.Resumed;
                    }
                    // Trip already InProgress — backstop a MISSED reassignment
                    // TASK_PROCESSING webhook. RIOT3's order-level resolved
                    // vehicle is the current robot; keep DTMS's cache pointer in
                    // sync so operator PASS/CANCEL commands (and the trip board)
                    // target the robot actually running the job. Idempotent —
                    // no-ops (returns false) when the robot is unchanged, so a
                    // steady-state poll produces no transition.
                    if (trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.InProgress
                        && trip.ReconcileVehicleAssignment(vehKey, vehName, source: "reconciler"))
                    {
                        return Transition.VehicleReassigned;
                    }
                    return Transition.None;

                case "HANG":
                case "HELD":
                    // Vendor paused — transition from InProgress (fresh pause)
                    // or from the OTHER pause flavour (vendor re-flavoured the
                    // pause mid-way; Trip.Pause handles the drift and flags the
                    // event Reflavour). Same flavour again / Created / terminal
                    // are no-ops. The status now carries the flavour that the
                    // resume command derives CONTINUE_FROM_* from.
                    {
                        var pauseSource = state == "HANG"
                            ? DTMS.Dispatch.Domain.Enums.VendorPauseSource.Hang
                            : DTMS.Dispatch.Domain.Enums.VendorPauseSource.Held;
                        var pauseTarget = state == "HANG"
                            ? DTMS.Dispatch.Domain.Enums.TripStatus.Hang
                            : DTMS.Dispatch.Domain.Enums.TripStatus.Held;
                        var eligible = trip.Status == DTMS.Dispatch.Domain.Enums.TripStatus.InProgress
                            || (trip.Status is DTMS.Dispatch.Domain.Enums.TripStatus.Hang
                                             or DTMS.Dispatch.Domain.Enums.TripStatus.Held
                                && trip.Status != pauseTarget);
                        if (eligible)
                        {
                            trip.Pause(pauseSource);
                            return pauseTarget == DTMS.Dispatch.Domain.Enums.TripStatus.Hang
                                ? Transition.Hang
                                : Transition.Held;
                        }
                        return Transition.None;
                    }

                case "REJECTED":
                    // Vendor refused the task post-dispatch, before execution.
                    // Distinct Rejected status (order-side propagation still
                    // mirrors Failed via TripRejectedIntegrationEventV1).
                    var rejectReason = data.FailReason?.ErrorDescription
                                       ?? data.FailReason?.ErrorCode
                                       ?? "vendor rejected task";
                    trip.MarkVendorRejected(rejectReason);
                    return Transition.Rejected;

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
                var expectedCompletion = Riot3MissionEventFactory.ParseRiot3Time(data.OrderStateChangeTime ?? data.FinalTime);
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
            catch (DbUpdateConcurrencyException)
            {
                // Concurrent writer beat this save. Same shared-context
                // poisoning as the tick loop (failed save leaves Added
                // OutboxMessages + a Modified trip in the tracker), so stop
                // the sweep here — no further saves happen on this scope
                // after self-heal, and the next tick reloads fresh.
                // MUST precede the generic catch below.
                _logger.LogInformation(
                    "[Reconciler] Self-heal Trip {TripId} lost a write race — sweep stopped, next tick retries", trip.Id);
                return healed;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[Reconciler] Self-heal failed for Trip {TripId} — will retry next tick", trip.Id);
            }
        }
        return healed;
    }

    // Tick-abort path for optimistic-concurrency conflicts: a webhook (or
    // the snapshot consumer) committed a competing write while this tick
    // held stale tracked state. Records the tick metric before returning so
    // the trips_stuck gauge doesn't go stale when conflicts cluster.
    private void AbortTickOnConflict(
        Trip trip, int stale, int fresh,
        int reconciled, int fetchErrors, int backfilled)
    {
        _logger.LogInformation(
            "[Reconciler] Trip {TripId} (upperKey {UpperKey}) lost a write race to a concurrent writer — aborting tick, next tick reconciles",
            trip.Id, trip.UpperKey);
        _metrics.RecordReconcilerTick(tripsStuck: stale, inflight: fresh,
            reconciled: reconciled, fetchErrors: fetchErrors, backfilled: backfilled);
    }

    // ── Mission diff + final snapshot helpers ────────────────────────────

    // internal (not private) so the unit tests can assert the mission upsert
    // records the REAL vendor time (finishedTime/startedTime) instead of the
    // poll instant — the bug that collapsed the timeline ordering.
    internal static async Task UpsertMissionsAsync(
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
                // Field semantics (station-by-type, time-by-state) live in the
                // shared factory so this path can never drift from the webhook.
                var ev = Riot3MissionEventFactory.Create(
                    tripId: tripId,
                    missionIndex: m.MissionIndex ?? i,
                    missionKey: m.MissionKey!,
                    missionType: m.Type,
                    state: state,
                    startedTime: m.StartedTime,
                    finishedTime: m.FinishedTime,
                    stationName: m.StationName,
                    actionName: m.ActionName,
                    actionType: m.ActionType,
                    resultCode: m.ResultCode,
                    errorMessage: m.ResultStr,
                    // Order-GET ACT missions carry no station (null already) —
                    // the factory's discarded-station debug log has nothing to
                    // say on this path, so a null logger keeps the static
                    // signature unchanged for the unit tests.
                    logger: Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
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

    // Pickup/drop detection safety net for dropped sub-task webhooks. Runs the
    // SAME station-match logic as the webhook (TripStationTransitionDetector)
    // over the order-query missions, using each mission's real time. Returns
    // true if a pickup or drop actually fired so the caller persists the Trip.
    //
    // Cheap-skip once both signals have fired: the per-mission helper early-outs
    // anyway (fire-once), but bailing here avoids the station/POD lookups on
    // every steady-state poll after pickup+drop are both done.
    internal async Task<bool> DetectStationTransitionsAsync(
        Trip trip,
        Riot3OrderQueryData data,
        DTMS.Facility.Application.Services.IFacilityReadService facilityReadService,
        DTMS.Dispatch.Application.Services.IDeliveryOrderStatusReader orderReader,
        CancellationToken ct)
    {
        if (trip.VendorPickedUpAt is not null && trip.VendorDroppedAt is not null) return false;
        if (data.Missions is null || data.Missions.Count == 0) return false;

        var fired = false;
        foreach (var m in data.Missions)
        {
            if (ct.IsCancellationRequested) break;
            // Detector only accepts FINISHED missions, so finishedTime IS the
            // acted-at moment (MissionChangeTime's blanket fallback is gone).
            var actedAt = Riot3MissionEventFactory.ParseRiot3Time(m.FinishedTime);
            if (await TripStationTransitionDetector.TryApplyAsync(
                    trip, m.Type, m.State, m.StationId,
                    facilityReadService, orderReader, actedAt, _logger, ct))
            {
                fired = true;
            }
        }
        return fired;
    }

    private static bool IsTerminalVendorState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        var s = state.ToUpperInvariant();
        // "SUCCEEDED" is the order-level orderState success token (the notify
        // task.state uses "FINISHED"); both must gate snapshot + vehicle backfill.
        return s is "FINISHED" or "SUCCEEDED" or "FAILED" or "CANCELED" or "CANCELLED" or "REJECTED";
    }

    internal enum Transition { None, Completed, Failed, Rejected, Cancelled, Started, Hang, Held, Resumed, VehicleReassigned }
}
