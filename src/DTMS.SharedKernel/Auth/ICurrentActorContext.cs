namespace DTMS.SharedKernel.Auth;

/// <summary>
/// Ambient accessor for the current <see cref="ActorContext"/>. Resolved
/// per-request (HTTP) or per-consume (MassTransit) — never a singleton's
/// static state, so unrelated concurrent operations stay isolated.
///
/// Lookup priority (implementations should follow):
///   1. Explicit <see cref="BeginScope"/> override (consumers, background jobs)
///   2. Ambient HTTP context (JWT name claim)
///   3. <see cref="ActorContext.System"/> fallback
/// </summary>
public interface ICurrentActorContext
{
    ActorContext Current { get; }

    /// <summary>
    /// Push an explicit actor context onto the ambient stack — used by
    /// MassTransit consumers + background services that don't have an
    /// <c>HttpContext</c>. Disposing the returned token restores the
    /// previous value, so usage MUST be inside a <c>using</c> block.
    /// </summary>
    IDisposable BeginScope(ActorContext context);
}
