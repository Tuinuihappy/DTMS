using System.Diagnostics.Metrics;
using System.Threading.Channels;
using DTMS.SharedKernel.Logging;

namespace DTMS.Infrastructure.Logging;

/// <summary>
/// Metrics for every <see cref="BatchedLogWriter{T}"/> (today: iam.SystemRequestLog,
/// the record of what each source system called). Non-generic so there is one
/// Meter, not one per entry type. Registered in Program.cs.
/// <list type="bullet">
/// <item><c>dtms.logwriter.enqueued{type}</c> — entries accepted.</item>
/// <item><c>dtms.logwriter.dropped{type,reason}</c> — entries that never reached
/// the database: <c>buffer_full</c> (evicted by DropOldest), <c>flush_failed</c>
/// (the sink threw, the whole batch is lost), <c>completed</c> (written after
/// shutdown began).</item>
/// </list>
/// </summary>
public static class BatchedLogMetrics
{
    public const string MeterName = "DTMS.Infrastructure.Logging";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Enqueued = Meter.CreateCounter<long>("dtms.logwriter.enqueued");
    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>("dtms.logwriter.dropped");

    internal static void RecordEnqueued(string type)
        => Enqueued.Add(1, new KeyValuePair<string, object?>("type", type));

    internal static void RecordDropped(string type, string reason, long count = 1)
        => Dropped.Add(count,
            new KeyValuePair<string, object?>("type", type),
            new KeyValuePair<string, object?>("reason", reason));
}

public sealed class BatchedLogWriter<T> : IBatchedLogWriter<T>
{
    private readonly Channel<T> _channel;
    private readonly string _entryTypeName = typeof(T).Name;

    public BatchedLogWriter(BatchedLogWriterOptions<T> opts)
    {
        _channel = Channel.CreateBounded<T>(
            new BoundedChannelOptions(opts.Capacity)
            {
                // At capacity we evict the oldest entry rather than block the
                // producer or fail the write. Producers are typically request
                // middleware that must not pay latency for log durability.
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            },
            // DropOldest evicts silently and TryWrite still returns true, so
            // this callback is the only place an eviction is visible. Without
            // it the "dropped" counter stayed at 0 while entries were lost.
            itemDropped: _ => BatchedLogMetrics.RecordDropped(_entryTypeName, "buffer_full"));
    }

    internal ChannelReader<T> Reader => _channel.Reader;

    internal void Complete() => _channel.Writer.TryComplete();

    public bool Enqueue(T entry)
    {
        // False only once the writer is completed (shutdown); a full buffer
        // still returns true and is counted by itemDropped above.
        if (_channel.Writer.TryWrite(entry))
        {
            BatchedLogMetrics.RecordEnqueued(_entryTypeName);
            return true;
        }

        BatchedLogMetrics.RecordDropped(_entryTypeName, "completed");
        return false;
    }
}

public sealed class BatchedLogWriterOptions<T>
{
    public int Capacity { get; init; } = 10_000;
    public int MaxBatchSize { get; init; } = 200;
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(5);
}
