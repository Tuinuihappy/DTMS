namespace DTMS.SharedKernel.Auth;

/// <summary>
/// Snapshot of "who triggered this operation" carried through the request
/// pipeline + ambient context. Used by Event Projection (Phase P0) to
/// populate <c>TriggeredBy</c> on history rows so projection-based audits
/// can answer "who changed status X" without joining other tables.
///
/// Sources are intentionally a small closed set so reports can pivot on
/// them cleanly:
///   - "http"            — synchronous user action (POST /orders/{id}/cancel)
///   - "vendor-webhook"  — RIOT3 callback, OMS callback, etc.
///   - "scheduled-job"   — outbox processor, reconciliation poller, SLA risk
///   - "system"          — fallback for non-attributable transitions
/// </summary>
public sealed record ActorContext(
    string? UserId,
    string Source,
    Guid? CorrelationId)
{
    /// <summary>System default — used when no ambient/HTTP context exists.</summary>
    public static ActorContext System { get; } = new(null, "system", null);

    /// <summary>
    /// The string that lands in <c>history.triggered_by</c>. Prefers the
    /// real user id; falls back to the source channel so the row is never
    /// silently <c>null</c>.
    /// </summary>
    public string TriggeredBy => string.IsNullOrWhiteSpace(UserId) ? Source : UserId!;
}
