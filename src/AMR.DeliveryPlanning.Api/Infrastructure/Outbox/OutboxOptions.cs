namespace AMR.DeliveryPlanning.Api.Infrastructure.Outbox;

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
}
