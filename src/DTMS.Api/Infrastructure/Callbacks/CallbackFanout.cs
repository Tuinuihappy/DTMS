using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>Shared helpers for the callback fan-out consumers.</summary>
internal static class CallbackFanout
{
    /// <summary>
    /// Stable CorrelationId for callbacks where DISTINCT integration events
    /// must still collapse to one outbox row per (system, semantic action) —
    /// e.g. the operator cancel and RIOT3's TASK_CANCELED echo both raise
    /// TripCancelledIntegrationEvent for the same trip (the webhook races
    /// Trip.Cancel's status guard, so both commit). MessageId-based
    /// correlation keeps those apart; hashing the semantic key instead lets
    /// the partial unique index on (PartitionKey, CorrelationId) reject the
    /// second row as a duplicate. Layout is v5-style (SHA-1, version/variant
    /// bits set) but only uniqueness and stability matter here — the value
    /// never leaves the outbox table.
    /// </summary>
    public static Guid DeterministicCorrelationId(string semanticKey)
    {
        var bytes = SHA1.HashData(
            Encoding.UTF8.GetBytes("dtms.callback-fanout:" + semanticKey))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

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
