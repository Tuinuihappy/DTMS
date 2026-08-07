using System.Security.Claims;
using DTMS.Iam.Application.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Per-request claims transformation for SYSTEM principals: looks up the
/// permissions granted to the system client (JWT with <c>sub</c> of form
/// <c>system:{key}</c>) in iam.SystemClientPermissions and stamps them
/// onto the ClaimsPrincipal as "permission" claims.
/// <see cref="PermissionAuthorizationHandler"/> then reads these claims
/// to evaluate <c>.RequirePermission(...)</c>.
///
/// <para><b>User principals are not transformed.</b> External Auth
/// (ADR-014) is the sole source of truth for user permissions: the LDAP
/// JWT carries a <c>permission</c> claim array which JwtBearer
/// (<c>MapInboundClaims=false</c>) surfaces directly on the identity.
/// The iam.RolePermissions role lookup that used to run here was removed
/// 2026-08-01 together with the Roles admin surface — grant users
/// <c>dtms:*</c> (or granular codes) on the External Auth side.</para>
///
/// Lookups are cached for 5 minutes — the request hot path stays
/// in-memory, and grants/revokes via Admin UI take effect within the
/// TTL without forcing token re-issue.
/// </summary>
public sealed class PermissionClaimsTransformer : IClaimsTransformation
{
    public const string PermissionClaimType = "permission";
    private const string SystemSubjectPrefix = "system:";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ISystemClientRepository _systemClients;
    private readonly IMemoryCache _cache;

    public PermissionClaimsTransformer(
        ISystemClientRepository systemClients,
        IMemoryCache cache)
    {
        _systemClients = systemClients;
        _cache = cache;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return principal;

        // Only system principals are transformed. User tokens carry their
        // permission claims inline and pass through untouched.
        var sub = identity.FindFirst("sub")?.Value;
        if (sub is null || !sub.StartsWith(SystemSubjectPrefix, StringComparison.Ordinal))
            return principal;

        // IClaimsTransformation runs on every request, including ones that
        // already have permission claims (SignalR re-auth during a hub
        // connection lifecycle, OR SystemClientAuthMiddleware already
        // stamped them for /source/* paths). Bail out if we've already
        // populated them to avoid duplicate claims piling up.
        if (identity.HasClaim(c => c.Type == PermissionClaimType))
            return principal;

        var systemKey = sub[SystemSubjectPrefix.Length..];
        if (string.IsNullOrWhiteSpace(systemKey)) return principal;

        var systemCodes = await _cache.GetOrCreateAsync(
            $"iam:sys-perms:{systemKey}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                return await _systemClients.GetPermissionCodesAsync(systemKey);
            }) ?? Array.Empty<string>();

        foreach (var code in systemCodes)
            identity.AddClaim(new Claim(PermissionClaimType, code));

        return principal;
    }
}
