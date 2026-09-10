using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace DTMS.SharedKernel.Projection;

/// <summary>
/// One place to decide whether a projection failure deserves a retry.
///
/// Every projector used to carry its own copy of this rule, and every copy
/// was missing the same case: a unique-key violation. Two events for the
/// same brand-new key arrive milliseconds apart, both read "no row yet",
/// both INSERT, and the loser takes a 23505. That is the most ordinary
/// thing that can happen to a projection, and each copy classified it as a
/// permanent failure — logged "event dropped" and swallowed, so the message
/// was acked and never retried (observed 2026-09-09 on OrderListView).
///
/// Retrying is the right answer: the retry re-reads, finds the row the
/// winner wrote, and takes the update path. Nothing is lost and the log
/// stops crying wolf — which matters because a REAL permanent failure of
/// the same event type produces the identical line, and used to be
/// indistinguishable from routine racing.
///
/// Note this does not stop the race; it makes losing it harmless. Removing
/// the race itself means an atomic upsert in the store — a separate, larger
/// change (5 stores share the read-then-insert shape).
/// </summary>
public static class ProjectionFaults
{
    /// <summary>SQLSTATE 23505 — unique_violation. ANSI, not Postgres-specific.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>
    /// True when the failure is worth retrying. Callers rethrow on true so
    /// MassTransit's retry ladder takes over, and log + drop on false.
    /// </summary>
    public static bool IsTransient(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => true,
        TimeoutException => true,
        TaskCanceledException => true,
        // DbException.SqlState is BCL surface (System.Data.Common), so this
        // stays provider-agnostic — no Npgsql reference in SharedKernel.
        DbUpdateException dbe => HasUniqueViolation(dbe),
        _ => false,
    };

    // The provider wraps its own exception inside DbUpdateException, and can
    // nest further, so walk the chain rather than checking one level.
    private static bool HasUniqueViolation(Exception ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is DbException { SqlState: UniqueViolation })
                return true;
        }
        return false;
    }
}
