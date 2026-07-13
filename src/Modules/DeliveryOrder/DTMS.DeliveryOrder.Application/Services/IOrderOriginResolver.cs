namespace DTMS.DeliveryOrder.Application.Services;

/// <summary>
/// Snapshot of the source system that originated an order — the pair
/// stamped onto <c>DeliveryOrder.SourceSystemKey</c> +
/// <c>SourceSystemDisplayName</c> at create time. Immutable per-row
/// (audit-preserving) — later renames of the SystemClient row do not
/// retro-update orders.
/// </summary>
/// <param name="Key">Lowercase slug matching <c>iam.SystemClients.Key</c>.</param>
/// <param name="DisplayName">Human-readable label captured at create time.</param>
public sealed record OrderOrigin(string Key, string DisplayName);

/// <summary>
/// Server-side stamper for the origin identity of a new order. The
/// handler layer calls this instead of trusting anything on the wire
/// — clients (UI + external) cannot send <c>sourceSystem</c> and cannot
/// spoof origin. Two entry points reflect the two provenance flows:
/// <list type="bullet">
///   <item><see cref="GetInternalAsync"/> — UI / operator flow, a fixed
///   <c>internal</c> origin (no SystemClients row — the operator is on
///   the order's CreatedBy).</item>
///   <item><see cref="GetByKeyAsync"/> — external-system flow, resolves
///   the URL <c>{key}</c> segment (already authenticated + validated
///   by the middleware). Returns <c>null</c> if the client vanished
///   between auth and handler execution (admin race — caller treats
///   as failure).</item>
/// </list>
/// </summary>
public interface IOrderOriginResolver
{
    /// <summary>
    /// UI / operator flow. Returns the fixed <c>internal</c> origin — it is
    /// not an external system and has no <c>iam.SystemClients</c> row.
    /// </summary>
    Task<OrderOrigin> GetInternalAsync(CancellationToken ct = default);

    /// <summary>
    /// External-system flow. <paramref name="key"/> is the URL segment
    /// already validated by <c>SystemClientAuthMiddleware</c>. Returns
    /// <c>null</c> when the SystemClient row disappeared between auth
    /// and handler execution — caller returns a failure result.
    /// </summary>
    Task<OrderOrigin?> GetByKeyAsync(string key, CancellationToken ct = default);
}
