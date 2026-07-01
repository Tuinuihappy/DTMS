using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Resolves <see cref="SourceSystemPermissionRequirement.Template"/> by
/// substituting the caller's system key (from the authenticated
/// <see cref="SystemPrincipal"/>), then evaluates the resulting concrete
/// permission code against the caller's stamped permission claims — same
/// matcher (<see cref="PermissionAuthorizationHandler"/>) that drives the
/// static <see cref="PermissionRequirement"/>.
///
/// <para>Wildcards held by the caller (e.g. <c>dtms:source:*</c>) still
/// expand here because we delegate to the same matcher.</para>
///
/// <para><b>Phase S.8e — key source changed.</b> Historically this
/// handler read the key from the <c>{key}</c> route segment of
/// <c>/api/v1/source/{key}/*</c>. That coupled permission enforcement to
/// an unauthenticated input (the URL) even though the JWT sub was the
/// cryptographically authoritative identity — two trust boundaries for
/// one concept. The handler now takes the key from the
/// <see cref="SystemPrincipal"/> that <c>SystemClientAuthMiddleware</c>
/// stamps into <c>HttpContext.Items</c>, so:
/// <list type="bullet">
///   <item>the URL <c>{key}</c> segment can go away entirely (P3) without
///     breaking permission checks;</item>
///   <item>the new URL-less endpoint added in P2
///     (<c>POST /api/v1/source/delivery-orders</c>) works out of the box —
///     no <c>{key}</c> route value exists there, but the principal always
///     does once the middleware has run.</item>
/// </list></para>
///
/// <para>Security: the slug is still validated against the strict
/// alphanumeric-and-dash rule before substitution. It comes from
/// <c>SystemClient.Key</c> (constructor-validated, DB-stored) via the
/// principal — cleaner than trusting a URL segment, but the guard stays
/// as belt-and-suspenders for the case where an admin somehow inserts a
/// row with a bad key straight into the DB.</para>
/// </summary>
public sealed class SourceSystemPermissionHandler
    : AuthorizationHandler<SourceSystemPermissionRequirement>
{
    private readonly IHttpContextAccessor _http;

    public SourceSystemPermissionHandler(IHttpContextAccessor http)
    {
        _http = http;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SourceSystemPermissionRequirement requirement)
    {
        var httpCtx = _http.HttpContext;
        if (httpCtx is null)
            return Task.CompletedTask;

        // Phase S.8e — key from authenticated principal, not URL route.
        // The item key is a plain string ("principal") shared with
        // SystemClientAuthMiddleware; kept as string literal here to
        // avoid a project reference from Iam back to DTMS.Api.
        var systemKey = (httpCtx.Items["principal"] as SystemPrincipal)?.Key;
        if (!IsValidKey(systemKey))
            return Task.CompletedTask;

        var needed = StandardSystemPermissions.Resolve(requirement.Template, systemKey!);

        var held = context.User.FindAll(PermissionClaimsTransformer.PermissionClaimType);
        foreach (var claim in held)
        {
            if (Matches(claim.Value, needed))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }
        return Task.CompletedTask;
    }

    // Same matcher shape as PermissionAuthorizationHandler — kept private
    // here to avoid forcing that class to expose internals just for this
    // sibling. Two short copies beats a leaky abstraction.
    private static bool Matches(string held, string required)
    {
        if (held == required) return true;
        if (!held.EndsWith(":*", StringComparison.Ordinal)) return false;
        var prefix = held[..^1];
        return required.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Slugs follow the same lowercase-alphanumeric-and-dash rule the
    /// <see cref="DTMS.Iam.Domain.Entities.SystemClient"/> constructor
    /// enforces (no '/', '?', '#', '%', '\\', or spaces) — and we tighten
    /// it further to reject ':' and '*' so the substituted permission
    /// string can't become an unintended wildcard or carry an extra
    /// segment.
    /// </summary>
    internal static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 50)
            return false;
        foreach (var c in key)
        {
            var ok = (c >= 'a' && c <= 'z')
                  || (c >= '0' && c <= '9')
                  || c == '-';
            if (!ok) return false;
        }
        return true;
    }
}
