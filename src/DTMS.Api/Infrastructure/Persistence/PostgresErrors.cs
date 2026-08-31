using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Persistence;

/// <summary>
/// Shared recognition of PostgreSQL error conditions that surface through EF as
/// <see cref="DbUpdateException"/>. Lives in the API host because Npgsql is a
/// host-level dependency — SharedKernel deliberately doesn't reference it.
///
/// <para>Consolidates two copies that had drifted: the callback-fanout one
/// walked the whole inner-exception chain, the dead-letter one only checked the
/// immediate inner. The chain walk is kept — EF wraps provider exceptions at
/// varying depth depending on whether the failure came from SaveChanges, a
/// retrying execution strategy, or a batched update.</para>
/// </summary>
public static class PostgresErrors
{
    /// <summary>unique_violation — a UNIQUE index or constraint rejected the write.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>
    /// True when the failure is a unique-constraint rejection. Callers treat this
    /// either as an idempotent no-op (outbox/callback replays, where the duplicate
    /// IS the desired end state) or as a 409 to the caller (interactive writes,
    /// where someone else won the race) — never as a server fault.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var cur = ex.InnerException; cur is not null; cur = cur.InnerException)
        {
            if (cur is Npgsql.PostgresException pg && pg.SqlState == UniqueViolation)
                return true;
        }
        return false;
    }
}
