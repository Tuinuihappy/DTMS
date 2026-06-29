namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// One row per inbound request from a system principal — written by
/// the request-logging middleware via the batched log writer, drained
/// in 200-row chunks by a background sink. The table is partitioned
/// monthly by <see cref="OccurredAt"/>; PartitionMaintenanceService
/// rolls partitions forward and drops anything older than 90 days.
/// </summary>
public sealed class SystemRequestLogEntry
{
    public Guid Id { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string SystemKey { get; private set; } = string.Empty;
    public string Method { get; private set; } = string.Empty;
    public string Path { get; private set; } = string.Empty;
    public int StatusCode { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? CorrelationId { get; private set; }
    public int? DurationMs { get; private set; }

    private SystemRequestLogEntry() { }

    public SystemRequestLogEntry(
        Guid id,
        DateTime occurredAt,
        string systemKey,
        string method,
        string path,
        int statusCode,
        string? idempotencyKey,
        string? correlationId,
        int? durationMs)
    {
        Id = id;
        OccurredAt = occurredAt;
        SystemKey = systemKey;
        Method = method;
        Path = path;
        StatusCode = statusCode;
        IdempotencyKey = idempotencyKey;
        CorrelationId = correlationId;
        DurationMs = durationMs;
    }
}
