using DTMS.SharedKernel.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DTMS.Infrastructure.Logging;

/// <summary>
/// Reads from a <see cref="BatchedLogWriter{T}"/>'s channel and hands
/// the entries off to the configured <see cref="IBatchedLogSink{T}"/> in
/// batches. A scope is created per flush so the sink can resolve
/// scoped dependencies (a per-request <c>DbContext</c>, for example).
/// </summary>
public sealed class BatchedLogDrainService<T> : BackgroundService
{
    private readonly BatchedLogWriter<T> _writer;
    private readonly BatchedLogWriterOptions<T> _opts;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatchedLogDrainService<T>> _log;

    public BatchedLogDrainService(
        BatchedLogWriter<T> writer,
        BatchedLogWriterOptions<T> opts,
        IServiceScopeFactory scopeFactory,
        ILogger<BatchedLogDrainService<T>> log)
    {
        _writer = writer;
        _opts = opts;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<T>(_opts.MaxBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            buffer.Clear();
            try
            {
                await CollectBatchAsync(buffer, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (buffer.Count > 0)
                await FlushSafelyAsync(buffer, stoppingToken);
        }

        await DrainRemainingOnShutdownAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new writes — the drain loop above will detect
        // channel completion and exit, then we flush whatever remains.
        _writer.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task CollectBatchAsync(List<T> buffer, CancellationToken ct)
    {
        // Block until first entry arrives, then opportunistically pull
        // more until we reach max size or max wait elapses. The mixed
        // pull mode is what gives this writer its throughput: small
        // bursts flush fast, large bursts amortize into batches.
        if (!await _writer.Reader.WaitToReadAsync(ct))
            return;

        if (!_writer.Reader.TryRead(out var first))
            return;
        buffer.Add(first);

        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        batchCts.CancelAfter(_opts.MaxWait);

        while (buffer.Count < _opts.MaxBatchSize)
        {
            if (_writer.Reader.TryRead(out var next))
            {
                buffer.Add(next);
                continue;
            }

            try
            {
                if (!await _writer.Reader.WaitToReadAsync(batchCts.Token))
                    return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // MaxWait elapsed — flush whatever we have.
                return;
            }
        }
    }

    private async Task FlushSafelyAsync(List<T> batch, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sink = scope.ServiceProvider.GetRequiredService<IBatchedLogSink<T>>();
            await sink.FlushAsync(batch, ct);
        }
        catch (Exception ex)
        {
            // Don't propagate — that would tear the drain loop down and
            // grow the backlog. Operator sees the exception in logs and
            // can investigate; lost entries here are bounded by batch size.
            _log.LogError(ex,
                "Batched log drain for {Type} failed to flush {Count} entries",
                typeof(T).Name, batch.Count);
        }
    }

    private async Task DrainRemainingOnShutdownAsync()
    {
        var remaining = new List<T>();
        while (_writer.Reader.TryRead(out var entry))
            remaining.Add(entry);

        if (remaining.Count == 0)
            return;

        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await FlushSafelyAsync(remaining, shutdownCts.Token);
            _log.LogInformation(
                "Flushed {Count} remaining {Type} entries during shutdown",
                remaining.Count, typeof(T).Name);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Failed to flush {Count} remaining {Type} entries within shutdown window",
                remaining.Count, typeof(T).Name);
        }
    }
}
