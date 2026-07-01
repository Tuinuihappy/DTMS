using System.Security.Claims;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain;
using DTMS.Iam.Application.Authorization;

namespace DTMS.Api.Auth;

/// <summary>
/// Composition-root implementation of <see cref="IOrderOriginResolver"/>.
/// Lives here (not in DTMS.DeliveryOrder.Infrastructure) so the
/// DeliveryOrder module doesn't take a project reference on IAM — the
/// cross-module glue stays in the API host, same pattern as
/// <c>IWarehouseLookup</c>.
///
/// <para><b>External path optimization:</b> the middleware
/// (<see cref="Middlewares.SystemClientAuthMiddleware"/>) stamps
/// <see cref="ClaimTypes.Name"/> = <c>SystemClient.DisplayName</c> on
/// the ClaimsPrincipal at auth time. <see cref="GetByKeyAsync"/> reads
/// that claim first and only falls back to <see cref="CachedSystemClientReader"/>
/// when the claim is missing — which happens on the internal admin path
/// (no middleware pipeline) or during a race with client-row deletion.
/// The optimization skips ~1ms of L1-cache hit per external order-create
/// request without weakening the audit trail (both paths write the same
/// snapshot value).</para>
/// </summary>
public sealed class OrderOriginResolver : IOrderOriginResolver
{
    private readonly CachedSystemClientReader _clients;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderOriginResolver(
        CachedSystemClientReader clients,
        IHttpContextAccessor httpContextAccessor)
    {
        _clients = clients;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<OrderOrigin> GetManualAsync(CancellationToken ct = default)
    {
        var origin = await GetByKeyAsync(WellKnownSourceSystems.Manual, ct);
        if (origin is null)
            throw new InvalidOperationException(
                $"SystemClient row for '{WellKnownSourceSystems.Manual}' is missing. " +
                "Run migration 20260630020000_SeedManualSapErpSystemClients — the UI order path " +
                "cannot stamp origin without it.");
        return origin;
    }

    public async Task<OrderOrigin?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        // Fast path — the middleware stamped ClaimTypes.Name with the
        // SystemClient's DisplayName after JWT auth. Trust it when the
        // request context's principal matches the key being resolved
        // (guards against a handler ever resolving a different system
        // than the one authenticated on this request).
        var claimDisplayName = TryReadDisplayNameFromClaim(key);
        if (claimDisplayName is not null)
            return new OrderOrigin(key, claimDisplayName);

        // Slow path — no matching claim (UI request has no system
        // principal; or admin/background caller). Hit the cache.
        var client = await _clients.GetAsync(key, ct);
        if (client is null || !client.IsActive)
            return null;

        return new OrderOrigin(client.Key, client.DisplayName);
    }

    private string? TryReadDisplayNameFromClaim(string requestedKey)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity is not { IsAuthenticated: true }) return null;

        // Only trust the claim when the principal's system-key
        // matches what the caller asked to resolve. Prevents a stray
        // handler call with an arbitrary key from picking up an
        // unrelated system's DisplayName.
        var nameIdentifier = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (nameIdentifier is null ||
            !nameIdentifier.Equals($"system:{requestedKey}", StringComparison.Ordinal))
            return null;

        var displayName = user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }
}
