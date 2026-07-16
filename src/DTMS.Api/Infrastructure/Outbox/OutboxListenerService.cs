using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Fleet.Infrastructure.Data;
using DTMS.Planning.Infrastructure.Data;
using DTMS.SharedKernel.Outbox;
using DTMS.Transport.Amr.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Phase O2 — Postgres LISTEN/NOTIFY driver for the outbox processor.
/// Holds one dedicated <see cref="NpgsqlConnection"/> per module schema,
/// calls <c>LISTEN outbox_notify_&lt;schema&gt;</c>, and signals the
/// processor via <see cref="IOutboxWakeSignal"/> the instant a new row
/// commits.
///
/// <para><b>Why a dedicated connection per module.</b> Postgres LISTEN
/// is session-scoped; a pooled connection returned to the pool loses
/// its LISTEN state. So each module gets its own long-lived Npgsql
/// connection, opened at start, held until shutdown, reconnected on
/// disconnect.</para>
///
/// <para><b>Bypass pgbouncer.</b> pgbouncer transaction-mode does not
/// support LISTEN across transactions (LISTEN is session-scoped).
/// The listener therefore uses <c>ConnectionStrings:OutboxListener</c>
/// which points directly at <c>postgres:5432</c>, bypassing pgbouncer.
/// All other DbContexts continue through pgbouncer unchanged — this
/// is the only connection posture that dodges the pool.</para>
///
/// <para><b>Fail-safe design.</b> If the listener never wakes (missed
/// notification, connection down for 4s, etc.), the periodic poll in
/// <see cref="OutboxProcessorService"/> still runs every
/// <c>PollIntervalSeconds</c> and picks up the row. This service only
/// affects latency, never correctness.</para>
/// </summary>
public sealed class OutboxListenerService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IOptionsMonitor<OutboxOptions> _options;
    private readonly IOutboxWakeSignal _wakeSignal;
    private readonly ILogger<OutboxListenerService> _log;

    // The module schemas OutboxProcessorService drains — see
    // ProcessUnpublishedEventsAsync. We do NOT LISTEN on the central `outbox`
    // schema: its partitioned rows are driven by MultiPartitionOutboxProcessor's
    // own loop, and its null-partition rows (SourceCallbackOutcome etc.) are
    // drained by OutboxProcessorService's central pass on the poll timer. A
    // NOTIFY channel here would only shave poll latency off that audit-mirror
    // path — not a correctness gap — so it is intentionally left out for now.
    private static readonly string[] Schemas =
    {
        DeliveryOrderDbContext.Schema,
        PlanningDbContext.Schema,
        DispatchDbContext.Schema,
        FleetDbContext.Schema,
        VendorAdapterDbContext.Schema,
    };

    public OutboxListenerService(
        IConfiguration config,
        IOptionsMonitor<OutboxOptions> options,
        IOutboxWakeSignal wakeSignal,
        ILogger<OutboxListenerService> log)
    {
        _config = config;
        _options = options;
        _wakeSignal = wakeSignal;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.UseListenNotify)
        {
            _log.LogInformation("OutboxListenerService disabled (Outbox:UseListenNotify=false)");
            return;
        }

        // Prefer the dedicated listener connection string (direct postgres).
        // Fall back to the default connection if not configured — logged so
        // ops can spot the misconfiguration (pgbouncer will refuse LISTEN
        // in transaction mode and the loop will keep reconnecting).
        var connString = _config.GetConnectionString("OutboxListener")
                      ?? _config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connString))
        {
            _log.LogError("OutboxListenerService: no connection string configured (ConnectionStrings:OutboxListener nor :DefaultConnection). Disabling.");
            return;
        }

        _log.LogInformation("OutboxListenerService started ({SchemaCount} schemas, ReconnectSeconds={ReconnectSeconds})",
            Schemas.Length, opts.ListenReconnectSeconds);

        // One listener loop per schema — each on its own connection. Any
        // one loop failing doesn't touch the others. WhenAll waits until
        // stoppingToken fires (they never return normally).
        var tasks = Schemas.Select(schema => ListenLoopAsync(schema, connString, stoppingToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task ListenLoopAsync(string schema, string connString, CancellationToken ct)
    {
        var channel = OutboxNotificationChannel.ForSchema(schema);

        while (!ct.IsCancellationRequested)
        {
            var reconnectSeconds = Math.Max(1, _options.CurrentValue.ListenReconnectSeconds);
            try
            {
                await using var conn = new NpgsqlConnection(connString);
                await conn.OpenAsync(ct);

                // Npgsql raises the Notification event on the connection
                // when the server delivers a NOTIFY. Handler is nonblocking
                // — TryWrite on the bounded channel; drops silently on full
                // (see OutboxWakeSignal comments).
                conn.Notification += (_, args) =>
                {
                    _wakeSignal.Signal(args.Channel);
                };

                await using (var listenCmd = new NpgsqlCommand($"LISTEN \"{channel}\";", conn))
                {
                    await listenCmd.ExecuteNonQueryAsync(ct);
                }

                _log.LogInformation("Listening on {Channel}", channel);

                // Block until a notification arrives — WaitAsync returns when
                // Npgsql receives a NOTIFY. Loop repeats immediately, ready
                // for the next one. Exits only on cancellation or exception.
                while (!ct.IsCancellationRequested)
                {
                    await conn.WaitAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Listener for {Channel} disconnected — reconnecting in {Seconds}s",
                    channel, reconnectSeconds);

                try { await Task.Delay(TimeSpan.FromSeconds(reconnectSeconds), ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        _log.LogInformation("Listener for {Channel} stopped", channel);
    }
}
