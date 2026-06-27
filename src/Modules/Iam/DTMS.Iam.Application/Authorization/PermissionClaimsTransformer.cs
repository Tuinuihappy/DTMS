using System.Security.Claims;
using DTMS.Iam.Application.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Per-request claims transformation: looks up the permissions granted
/// to the caller's role and stamps them onto the ClaimsPrincipal as
/// "permission" claims. <see cref="PermissionAuthorizationHandler"/>
/// then reads these claims to evaluate <c>.RequirePermission(...)</c>.
///
/// Lookups are cached for 5 minutes per role — the request hot path
/// stays in-memory, and changes to role mappings (Phase B Admin UI)
/// take effect within the TTL without forcing logouts.
/// </summary>
public sealed class PermissionClaimsTransformer : IClaimsTransformation
{
    public const string PermissionClaimType = "permission";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPermissionRepository _permissions;
    private readonly IMemoryCache _cache;

    public PermissionClaimsTransformer(IPermissionRepository permissions, IMemoryCache cache)
    {
        _permissions = permissions;
        _cache = cache;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return principal;

        // IClaimsTransformation runs on every request, including ones
        // that already have permission claims (e.g. SignalR re-auth
        // during a hub connection lifecycle). Bail out if we've already
        // populated them to avoid duplicate claims piling up.
        if (identity.HasClaim(c => c.Type == PermissionClaimType))
            return principal;

        var role = identity.FindFirst(identity.RoleClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(role))
            return principal;

        var codes = await _cache.GetOrCreateAsync(
            $"iam:perms:{role}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                return await _permissions.GetPermissionCodesForRoleAsync(role);
            }) ?? Array.Empty<string>();

        foreach (var code in codes)
            identity.AddClaim(new Claim(PermissionClaimType, code));

        return principal;
    }
}
