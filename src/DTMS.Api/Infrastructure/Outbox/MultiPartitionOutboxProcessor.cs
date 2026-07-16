using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Channels;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Application.Repositories;
using DTMS.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Phase S.3 — drains outbox rows whose <see cref="OutboxMessage.PartitionKey"/>
/// is non-null (one row = one system callback) on a per-system worker
/// task. Sits alongside the legacy <see cref="OutboxProcessorService"/>
/// which keeps handling rows with <c>PartitionKey = NULL</c> (domain
/// integration events going through MassTransit) — the two never see
/// each other's rows because the partial index keys on PartitionKey.
/// </summary>
/// <remarks>
/// <para><b>Why one master + N workers (not N hosted services).</b>
/// <see cref="IHostedService"/> registrations are resolved at DI build
/// time; adding a system at runtime can't trigger a new hosted service
/// to spawn. The master owns a worker dictionary keyed by system slug,
/// reconciles it against <c>iam.SystemClients</c> every discovery
/// cycle (30 s) OR on Redis pub/sub trigger
/// (<c>iam:system:changed</c>) from admin mutations.</para>
///
/// <para><b>Multi-pod safety.</b> Each worker's claim query uses
/// <c>FOR UPDATE SKIP LOCKED</c> inside a transaction so two pods'
/// workers for the same system don't process the same row. Workers
/// run identical code on every pod; the database picks the winner
/// per row.</para>
///
/// <para><b>Dispatch contract.</b> Per row, the worker resolves an
/// <see cref="ISourceCallbackDispatcher"/> from a per-iteration scope
/// and calls <see cref="ISourceCallbackDispatcher.DispatchAsync"/>.
/// Success → mark processed inside the same transaction. Failure
/// (any exception, including the dispatcher's own retry exhausting)
/// → mark failed via <see cref="OutboxMessage.MarkAsFailed"/>, which
/// schedules a retry per <c>OutboxRetryPolicy</c>'s backoff curve
/// or marks terminal after max retries.</para>
/// </remarks>
public sealed class MultiPartitionOutboxProcessor : BackgroundService
{
    public const string DiscoveryChannel = "iam:system:changed";

    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CrashBackoff = TimeSpan.FromSeconds(5);
    private const int BatchLimit = 50;

    private readonly IServiceProvider _sp;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MultiPartitionOutboxProcessor> _log;

    private readonly ConcurrentDictionary<string, Task> _workers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _workerCts = new();

    // BoundedChannel(1) + DropWrite — multiple admin mutations that
    // queue up before the next discovery cycle collapse into a single
    // wake-up. The bool value is meaningless; only the signal matters.
    private readonly Channel<bool> _discoveryTrigger =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    public MultiPartitionOutboxProcessor(
        IServiceProvider sp,
        IConnectionMultiplexer redis,
        ILogger<MultiPartitionOutboxProcessor> log)
    {
        _sp = sp;
        _redis = redis;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _redis.GetSubscriber();
        await sub.SubscribeAsync(
            RedisChannel.Literal(DiscoveryChannel),
            (_, _) => _discoveryTrigger.Writer.TryWrite(true));

        _log.LogInformation(
            "MultiPartitionOutboxProcessor started. Subscribed to {Channel} + {Interval}s discovery interval.",
            DiscoveryChannel, (int)DiscoveryInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverAndReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Outbox discovery cycle threw. Will retry next interval.");
            }

            // Wake on Redis pub/sub OR on the 30s fallback interval —
            // 100% async path; no thread blocking even when idle.
            try
            {
                using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cycleCts.CancelAfter(DiscoveryInterval);
                await _discoveryTrigger.Reader.WaitToReadAsync(cycleCts.Token);
                // Drain any extra signals so we don't immediately
                // re-trigger another cycle for the same admin batch.
                while (_discoveryTrigger.Reader.TryRead(out _)) { }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Interval elapsed — expected path; loop continues.
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Shutdown — cancel all workers; wait briefly for graceful exit.
        foreach (var cts in _workerCts.Values)
            cts.Cancel();
        try
        {
            await Task.WhenAll(_workers.Values.ToArray()).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            _log.LogWarning("One or more outbox workers did not exit within shutdown window.");
        }

        _log.LogInformation("MultiPartitionOutboxProcessor stopped.");
    }

    private async Task DiscoverAndReconcileAsync(CancellationToken ct)
    {
        // Each cycle reads the system list from a fresh scope so a
        // crash or schema change doesn't poison the cached connection.
        using var scope = _sp.CreateScope();
        var systemRepo = scope.ServiceProvider.GetRequiredService<ISystemClientRepository>();
        var active = await systemRepo.ListActiveAsync(ct);
        var activeKeys = new HashSet<string>(active.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

        // Spawn workers for newly-active systems.
        foreach (var sys in active)
        {
            if (_workers.ContainsKey(sys.Key))
                continue;

            var workerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _workerCts[sys.Key] = workerCts;
            _workers[sys.Key] = Task.Run(
                () => ProcessPartitionLoopAsync(sys.Key, workerCts.Token),
                workerCts.Token);
            _log.LogInformation("Spawned outbox worker for system={Key}", sys.Key);
        }

        // Stop workers for deactivated / deleted systems.
        foreach (var key in _workers.Keys.ToList())
        {
            if (activeKeys.Contains(key))
                continue;

            if (_workerCts.TryRemove(key, out var cts))
                cts.Cancel();
            _workers.TryRemove(key, out _);
            _log.LogInformation("Stopped outbox worker for system={Key}", key);
        }
    }

    private async Task ProcessPartitionLoopAsync(string systemKey, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int processed = await ProcessOneBatchAsync(systemKey, ct);

                // No rows or only a few — wait before polling again so
                // we don't spin against an idle DB.
                if (processed < BatchLimit)
                    await Task.Delay(IdleBackoff, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Outbox worker {Key} batch failed; restarting in {Delay}s",
                    systemKey, (int)CrashBackoff.TotalSeconds);
                try { await Task.Delay(CrashBackoff, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        _log.LogDebug("Outbox worker {Key} exited", systemKey);
    }

    private async Task<int> ProcessOneBatchAsync(string systemKey, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISourceCallbackDispatcher>();

        // Outer execution strategy because the DbContext was registered
        // with EnableRetryOnFailure — explicit BeginTransactionAsync
        // outside the strategy would throw "this operation is not
        // supported by the configured execution strategy".
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);

            // Claim a batch under SKIP LOCKED so two pods don't both
            // try to dispatch the same row. The PartitionKey index +
            // tx scope keep contention bounded to one row at a time.
            var nowParam = DateTime.UtcNow;
            var batch = await db.OutboxMessages
                .FromSqlRaw(@"
                    SELECT *
                    FROM outbox.""OutboxMessages""
                    WHERE ""PartitionKey"" = {0}
                      AND ""ProcessedOnUtc"" IS NULL
                      AND (""NextRetryAtUtc"" IS NULL OR ""NextRetryAtUtc"" <= {1})
                    ORDER BY ""OccurredOnUtc""
                    LIMIT {2}
                    FOR UPDATE SKIP LOCKED",
                    systemKey, nowParam, BatchLimit)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }

            foreach (var msg in batch)
            {
                bool success;
                Exception? failure = null;
                try
                {
                    await dispatcher.DispatchAsync(systemKey, msg, ct);
                    msg.MarkAsProcessed(DateTime.UtcNow);
                    success = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Drop without saving — row stays locked until tx
                    // commits/rollbacks. Rollback releases the row to
                    // the next worker iteration.
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    // Classify before marking: a deterministic receiver
                    // rejection (e.g. 400 from OMS's create-once endpoint)
                    // goes terminal-in-place immediately instead of burning
                    // the full backoff (~2h45m) while head-blocking every
                    // good callback behind it in this ordered partition.
                    // Transient failures (401/403/404/408/429/5xx/timeouts/
                    // connection-level/config errors) keep the exact
                    // pre-classification MarkAsFailed behavior.
                    var permanent = HttpCallbackFailureClassifier.ApplyFailure(msg, ex, DateTime.UtcNow);
                    if (permanent)
                    {
                        // Warning, not Error: this is a recorded business
                        // outcome (the audit block below emits it and the
                        // order UI shows e.g. UpstreamOmsRejected), not
                        // infra trouble needing a page.
                        _log.LogWarning(ex,
                            "Dispatch permanently rejected for outbox row {Id} (system={SystemKey}, status={Status}, attempt={Attempt}); marked terminal without retry",
                            msg.Id, systemKey, (int?)(ex as HttpRequestException)?.StatusCode, msg.RetryCount);
                    }
                    else
                    {
                        _log.LogWarning(ex,
                            "Dispatch failed for outbox row {Id} (system={SystemKey}, attempt={Attempt})",
                            msg.Id, systemKey, msg.RetryCount);
                    }
                    success = false;
                    failure = ex;
                }

                // Phase S.5 — emit a dispatch-outcome for callback rows tied to
                // an order, so the owning module can write per-order audit. Fire
                // on success (once) or on TERMINAL failure only (retries
                // exhausted → MarkAsFailed set ProcessedOnUtc); a non-terminal
                // failure will retry, so we stay quiet. The row is a NULL-
                // partition outbox message written into this same `outbox`
                // schema: OutboxProcessorService's central pass drains
                // null-partition rows and publishes it through MassTransit (we
                // own only the partitioned rows here). It is written in the same
                // transaction as MarkAsProcessed — the callback result and its
                // audit-emit commit atomically.
                if (msg.RelatedOrderId is { } orderId && (success || msg.ProcessedOnUtc is not null))
                {
                    var statusCode = (failure as HttpRequestException)?.StatusCode;
                    var outcome = new SourceCallbackOutcome(
                        EventId: Guid.NewGuid(),
                        OccurredOn: DateTime.UtcNow,
                        SystemKey: systemKey,
                        CallbackEventType: msg.Type,
                        OrderId: orderId,
                        TripId: msg.RelatedTripId,
                        Success: success,
                        StatusCode: statusCode.HasValue ? (int)statusCode.Value : null,
                        Detail: success ? null : Truncate(failure?.Message),
                        CorrelationId: msg.CorrelationId);
                    db.OutboxMessages.Add(OutboxMessageFactory.FromIntegrationEvent(outcome));
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return batch.Count;
        });
    }

    private static string? Truncate(string? s) =>
        s is null ? null : s.Length <= 400 ? s : s[..400] + "…";
}
