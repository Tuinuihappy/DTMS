using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Data;
using DTMS.SharedKernel.Logging;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Iam.Infrastructure.Logging;

/// <summary>
/// Drains <see cref="SystemRequestLogEntry"/> batches from the
/// <see cref="IBatchedLogWriter{T}"/> Channel and bulk-inserts them
/// into the partitioned <c>iam.SystemRequestLog</c> table. Resolved
/// from a per-flush DI scope by the drain background service so the
/// <see cref="IamDbContext"/> lifetime stays bounded to one batch.
/// </summary>
public sealed class SystemRequestLogSink : IBatchedLogSink<SystemRequestLogEntry>
{
    private readonly IamDbContext _db;

    public SystemRequestLogSink(IamDbContext db) => _db = db;

    public async Task FlushAsync(IReadOnlyList<SystemRequestLogEntry> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        // AddRange + single SaveChanges produces one INSERT round-trip
        // for the whole batch (Npgsql's batch-insert is enabled by
        // default). Far cheaper than 200 separate INSERTs.
        await _db.SystemRequestLog.AddRangeAsync(batch, ct);
        await _db.SaveChangesAsync(ct);
    }
}
