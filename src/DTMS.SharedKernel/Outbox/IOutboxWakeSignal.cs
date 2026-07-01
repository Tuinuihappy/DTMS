using System.Threading.Channels;

namespace DTMS.SharedKernel.Outbox;

/// <summary>
/// Phase O2 — decouples the LISTEN/NOTIFY listener from the outbox
/// processor. The listener fires <see cref="Signal"/> when Postgres
/// delivers a notification; the processor's outer loop awaits
/// <see cref="WaitAsync"/> alongside its periodic poll so it wakes
/// on whichever event fires first.
///
/// <para><b>Fire-and-forget semantics.</b> If the channel buffer is
/// full (i.e. the processor is already awake and hasn't drained yet),
/// duplicate signals are silently dropped — the processor will still
/// discover the new row on its very next fetch, so extra wake-ups
/// buy nothing.</para>
/// </summary>
public interface IOutboxWakeSignal
{
    /// <summary>Nonblocking. Safe to call from Postgres notification callbacks.</summary>
    void Signal(string channelHint);

    /// <summary>Blocking — returns the channel name of the latest signal (hint only; caller drains all modules anyway).</summary>
    Task<string> WaitAsync(CancellationToken ct);
}

public sealed class OutboxWakeSignal : IOutboxWakeSignal
{
    // Bounded so a runaway notification storm can't grow the queue
    // unbounded. DropOldest keeps the freshest signal — losing an old
    // signal is safe because the processor drains ALL modules on any
    // wake, so the effective "which module needs work" hint is idempotent.
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(capacity: 64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    public void Signal(string channelHint) => _channel.Writer.TryWrite(channelHint);

    public Task<string> WaitAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct).AsTask();
}
