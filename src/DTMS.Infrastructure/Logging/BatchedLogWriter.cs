using System.Diagnostics.Metrics;
using System.Threading.Channels;
using DTMS.SharedKernel.Logging;

namespace DTMS.Infrastructure.Logging;

public sealed class BatchedLogWriter<T> : IBatchedLogWriter<T>
{
    public const string MeterName = "DTMS.Infrastructure.Logging";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DroppedCounter =
        Meter.CreateCounter<long>("dtms.logwriter.dropped");
    private static readonly Counter<long> EnqueuedCounter =
        Meter.CreateCounter<long>("dtms.logwriter.enqueued");

    private readonly Channel<T> _channel;
    private readonly string _entryTypeName = typeof(T).Name;

    public BatchedLogWriter(BatchedLogWriterOptions<T> opts)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(opts.Capacity)
        {
            // At capacity we evict the oldest entry rather than block the
            // producer or fail the write. Producers are typically request
            // middleware that must not pay latency for log durability.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    internal ChannelReader<T> Reader => _channel.Reader;

    internal void Complete() => _channel.Writer.TryComplete();

    public bool Enqueue(T entry)
    {
        // DropOldest channels never return false from TryWrite under
        // normal operation; false here means the writer has been
        // completed (StopAsync). Count both paths for visibility.
        if (_channel.Writer.TryWrite(entry))
        {
            EnqueuedCounter.Add(1, new KeyValuePair<string, object?>("type", _entryTypeName));
            return true;
        }

        DroppedCounter.Add(1, new KeyValuePair<string, object?>("type", _entryTypeName));
        return false;
    }
}

public sealed class BatchedLogWriterOptions<T>
{
    public int Capacity { get; init; } = 10_000;
    public int MaxBatchSize { get; init; } = 200;
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(5);
}
