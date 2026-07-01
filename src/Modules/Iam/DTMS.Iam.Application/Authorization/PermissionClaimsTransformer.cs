using System.Security.Claims;
using DTMS.Iam.Application.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Per-request claims transformation: looks up the permissions granted
/// to the caller and stamps them onto the ClaimsPrincipal as
/// "permission" claims. <see cref="PermissionAuthorizationHandler"/>
/// then reads these claims to evaluate <c>.RequirePermission(...)</c>.
///
/// <para><b>Two principal shapes handled:</b></para>
/// <list type="bullet">
///   <item><b>User</b> — role claim (from External Auth) → lookup
///   permissions by role in iam.RolePermissions.</item>
///   <item><b>System</b> — <c>sub</c> claim of form <c>system:{key}</c>
///   → lookup permissions in iam.SystemClientPermissions. Same claim
///   type + same authorization handler; permission strings are the ONLY
///   distinction between what a system can and cannot do at admin
///   endpoints (Phase S.8b — path-based boundary removed).</item>
/// </list>
///
/// Lookups are cached for 5 minutes — the request hot path stays
/// in-memory, and grants/revokes via Admin UI take effect within the
/// TTL without forcing logouts / token re-issue.
/// </summary>
public sealed class PermissionClaimsTransformer : IClaimsTransformation
{
    public const string PermissionClaimType = "permission";
    private const string SystemSubjectPrefix = "system:";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IPermissionRepository _permissions;
    private readonly ISystemClientRepository _systemClients;
    private readonly IMemoryCache _cache;

    public PermissionClaimsTransformer(
        IPermissionRepository permissions,
        ISystemClientRepository systemClients,
        IMemoryCache cache)
    {
        _permissions = permissions;
        _systemClients = systemClients;
        _cache = cache;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return principal;

        // IClaimsTransformation runs on every request, including ones
        // that already have permission claims (e.g. SignalR re-auth
        // during a hub connection lifecycle, OR SystemClientAuthMiddleware
        // already stamped them for /source/* paths). Bail out if we've
        // already populated them to avoid duplicate claims piling up.
        if (identity.HasClaim(c => c.Type == PermissionClaimType))
            return principal;

        // System principal path — JWT with sub = "system:{key}".
        // These tokens come through JwtBearer for /api/v1/* (Phase S.8b)
        // or through SystemClientAuthMiddleware for /api/v1/source/* — the
        // middleware stamps permission claims itself, so we only fall
        // through here on the JwtBearer path.
        var sub = identity.FindFirst("sub")?.Value;
        if (sub is not null && sub.StartsWith(SystemSubjectPrefix, StringComparison.Ordinal))
        {
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

        // User principal path — role-based lookup (unchanged).
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
