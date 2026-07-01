namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Runtime knobs for <see cref="OutboxProcessorService"/>. Bound to the
/// <c>Outbox</c> configuration section so ops can toggle the SKIP LOCKED path
/// or tune batch size without redeploying.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// When true, the per-module fetch uses raw SQL <c>FOR UPDATE SKIP LOCKED</c>
    /// inside an explicit transaction (rows are held until commit). This is the
    /// prerequisite for running multiple outbox workers without two workers
    /// fighting for the same row. Default false (legacy EF LINQ fetch path).
    /// </summary>
    public bool UseSkipLocked { get; set; } = false;

    /// <summary>
    /// Rows pulled per tick per module. Higher = better throughput at the cost
    /// of longer per-tick latency. Default 50 (matches the pre-flag hardcoded value).
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How many messages within a batch are published concurrently. Default 1
    /// (sequential — matches the pre-flag behaviour). Raising this trades CPU
    /// + RabbitMQ connection pressure for lower batch latency: with 50 messages
    /// where each takes ~50ms to publish, sequential is 2.5s while concurrency=8
    /// is ~315ms. Cap at the IBus connection pool size — going higher just
    /// queues at the bus client.
    /// </summary>
    public int PublishConcurrency { get; set; } = 1;

    /// <summary>
    /// How often the background loop polls each module for new outbox messages.
    /// Default 5s (matches the pre-flag hardcoded value). The drain ceiling per
    /// module is <c>BatchSize / PollIntervalSeconds</c> events/s — at the
    /// defaults that's 10/s/module = 60/s total across 6 modules. Under load
    /// scenarios where the create rate exceeds this (perf scenario B at 30 VU
    /// hits ~7500 events/s) drop to 1s and raise BatchSize to 500+ so drain
    /// matches generation. Minimum 1 — clamped at runtime to avoid a busy loop.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Per-message timeout passed to the <c>IPublishEndpoint.Publish</c> call.
    /// Fails the message (with retry) rather than blocking the batch when the
    /// bus is unavailable. Default 10s matches the pre-flag hardcoded value;
    /// raise if you've seen legitimate publishes taking longer (e.g. RabbitMQ
    /// flow control under sustained pressure).
    /// </summary>
    public int PerMessageTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Phase O2 — when true, <c>OutboxListenerService</c> holds a
    /// dedicated <c>NpgsqlConnection</c> per module schema, calls
    /// <c>LISTEN outbox_notify_&lt;schema&gt;</c>, and signals the processor
    /// via <c>IOutboxWakeSignal</c> the instant a new outbox row commits.
    /// The periodic poll (<see cref="PollIntervalSeconds"/>) remains as
    /// safety net — a missed notification (listener disconnect, unlucky
    /// timing) is picked up on the next poll ≤1s later, so this flag
    /// only affects latency, never correctness. Default true — flip off
    /// to fall back to poll-only behaviour without a redeploy.
    /// </summary>
    public bool UseListenNotify { get; set; } = true;

    /// <summary>
    /// How long the listener sleeps before reconnecting after its
    /// dedicated Npgsql connection drops (server bounce, network blip).
    /// Kept small — poll fallback covers the gap so a 5s reconnect
    /// window costs at most 5s of "back to poll cadence" for the
    /// affected module. Default 5s.
    /// </summary>
    public int ListenReconnectSeconds { get; set; } = 5;

    /// <summary>
    /// Phase O3 — base delay between ticks when messages ARE flowing.
    /// Default 100ms. On non-empty batch OR NOTIFY signal the delay
    /// resets to this floor. NOTIFY already fires the wake path so this
    /// mainly affects "just published a batch, is there more?" — a low
    /// value drains rapidly at the cost of DB queries.
    /// </summary>
    public int AdaptivePollBaseMs { get; set; } = 100;

    /// <summary>
    /// Phase O3 — ceiling on the adaptive-backoff delay when the outbox
    /// has been idle. Doubles per empty tick starting from
    /// <see cref="AdaptivePollBaseMs"/> and caps here. Default 5000ms
    /// (5s). Idle steady state = 25 queries/s → ~5 queries/s (5× less
    /// baseline load) while still detecting missed notifications
    /// within the safety window.
    /// </summary>
    public int AdaptivePollMaxMs { get; set; } = 5000;
}
