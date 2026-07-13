using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>Shared helpers for the callback fan-out consumers.</summary>
internal static class CallbackFanout
{
    /// <summary>
    /// Postgres unique-violation = SQLSTATE 23505. A consumer retry re-emits the
    /// same event (same MessageId → CorrelationId), so the partial unique index
    /// on <c>(PartitionKey, CorrelationId)</c> rejects the duplicate INSERT —
    /// caught here as an idempotent no-op.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var cur = ex.InnerException; cur is not null; cur = cur.InnerException)
        {
            if (cur is Npgsql.PostgresException pg && pg.SqlState == "23505")
                return true;
        }
        return false;
    }
}
