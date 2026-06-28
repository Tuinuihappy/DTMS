namespace DTMS.SharedKernel.Logging;

/// <summary>
/// Owner-supplied flush target for a <see cref="IBatchedLogWriter{T}"/>.
/// The drain service hands the sink a batch of entries to persist; the
/// sink decides how (bulk INSERT, COPY, append-only file, …). Errors
/// must be thrown so the drain service can log them — swallowing here
/// loses entries silently.
/// </summary>
public interface IBatchedLogSink<in T>
{
    Task FlushAsync(IReadOnlyList<T> batch, CancellationToken ct);
}
