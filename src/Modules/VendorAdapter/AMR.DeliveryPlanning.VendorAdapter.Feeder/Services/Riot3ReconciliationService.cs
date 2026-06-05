using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
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
        var queryService = scope.ServiceProvider.GetRequiredService<IRiot3OrderQueryService>();

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

            var transition = ApplyVendorState(trip, data);
            switch (transition)
            {
                case Transition.Completed: completed++; break;
                case Transition.Failed: failed++; break;
                case Transition.Cancelled: cancelled++; break;
                case Transition.Started: started++; break;
                case Transition.Paused: paused++; break;
                case Transition.Resumed: resumed++; break;
                case Transition.None: continue;
            }

            try
            {
                await tripRepo.UpdateAsync(trip, ct);
                reconciled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[Reconciler] persist failed for Trip {TripId} (upperKey {UpperKey})",
                    trip.Id, trip.UpperKey);
            }
        }

        var stale = await CountStaleTripsAsync(scope, staleCutoff, ct);
        _logger.LogInformation(
            "[Reconciler] tick: in-flight={InFlight} reconciled={Reconciled} (completed={Completed} failed={Failed} cancelled={Cancelled} started={Started} paused={Paused} resumed={Resumed}) noVendor={NoVendor} fetchErr={FetchErr} stale-skipped={Stale}",
            inFlight.Count, reconciled, completed, failed, cancelled, started, paused, resumed, skippedNoVendorRecord, skippedFetchError, stale);
    }

    private static Transition ApplyVendorState(Trip trip, Riot3OrderQueryData data)
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
                        Guid? vehicleId = null;
                        if (Guid.TryParse(data.ProcessingVehicle?.Key, out var v)) vehicleId = v;
                        trip.MarkVendorStarted(vehicleId);
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
                    if (trip.Status == AMR.DeliveryPlanning.Dispatch.Domain.Enums.TripStatus.InProgress)
                    {
                        trip.Pause();
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

    private enum Transition { None, Completed, Failed, Cancelled, Started, Paused, Resumed }
}
