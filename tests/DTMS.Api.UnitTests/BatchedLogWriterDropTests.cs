using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DTMS.Infrastructure.Logging;
using DTMS.SharedKernel.Logging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTMS.Api.UnitTests;

// iam.SystemRequestLog — the record of what OMS and other systems called — goes
// through a bounded DropOldest buffer. Its "dropped" counter only counted writes
// after shutdown: an eviction from a full buffer returns true from TryWrite, and
// a failed database flush wasn't counted at all, so lost rows read as zero.
public class BatchedLogWriterDropTests
{
    // Each test uses its own entry type so the "type" tag isolates its
    // measurements from other tests sharing the static meter.
    private sealed record BufferEntry(int N);
    private sealed record FlushEntry(int N);

    private static ConcurrentDictionary<string, long> ListenDropped(string type, out MeterListener listener)
    {
        var byReason = new ConcurrentDictionary<string, long>();
        listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BatchedLogMetrics.MeterName && instrument.Name == "dtms.logwriter.dropped")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? t = null, reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "type") t = tag.Value as string;
                if (tag.Key == "reason") reason = tag.Value as string;
            }
            if (t == type && reason is not null) byReason.AddOrUpdate(reason, value, (_, v) => v + value);
        });
        listener.Start();
        return byReason;
    }

    [Fact]
    public void FullBuffer_CountsEachEvictedEntry()
    {
        var dropped = ListenDropped(nameof(BufferEntry), out var listener);
        using var _ = listener;
        var writer = new BatchedLogWriter<BufferEntry>(new BatchedLogWriterOptions<BufferEntry> { Capacity = 2 });

        for (var i = 0; i < 5; i++)
            writer.Enqueue(new BufferEntry(i)).Should().BeTrue("DropOldest accepts the write and evicts instead");

        dropped.GetValueOrDefault("buffer_full").Should().Be(3);
    }

    private sealed class ThrowingSink : IBatchedLogSink<FlushEntry>
    {
        public Task FlushAsync(IReadOnlyList<FlushEntry> batch, CancellationToken ct)
            => throw new InvalidOperationException("db down");
    }

    [Fact]
    public async Task FailedFlush_CountsTheWholeBatch()
    {
        var dropped = ListenDropped(nameof(FlushEntry), out var listener);
        using var _ = listener;
        var opts = new BatchedLogWriterOptions<FlushEntry> { MaxBatchSize = 10, MaxWait = TimeSpan.FromMilliseconds(50) };
        var writer = new BatchedLogWriter<FlushEntry>(opts);
        var provider = new ServiceCollection()
            .AddScoped<IBatchedLogSink<FlushEntry>, ThrowingSink>()
            .BuildServiceProvider();
        var drain = new BatchedLogDrainService<FlushEntry>(
            writer, opts, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BatchedLogDrainService<FlushEntry>>.Instance);

        await drain.StartAsync(CancellationToken.None);
        for (var i = 0; i < 3; i++) writer.Enqueue(new FlushEntry(i));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dropped.GetValueOrDefault("flush_failed") < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await drain.StopAsync(CancellationToken.None);

        dropped.GetValueOrDefault("flush_failed").Should().Be(3);
    }
}
