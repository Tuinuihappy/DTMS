using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Resolves <see cref="SourceSystemPermissionRequirement.Template"/> by
/// substituting the <c>{key}</c> route value of the current HTTP request,
/// then evaluates the resulting concrete permission code against the
/// caller's stamped permission claims — same matcher
/// (<see cref="PermissionAuthorizationHandler"/>) that drives the static
/// <see cref="PermissionRequirement"/>.
///
/// <para>Wildcards held by the caller (e.g. <c>dtms:source:*</c>) still
/// expand here because we delegate to the same matcher.</para>
///
/// <para>Security: the route key is validated against a strict slug
/// regex before substitution. Without this, an attacker could craft
/// <c>/source/oms:*</c> and silently flip the template into a wildcard
/// that grants access to other systems' permissions.</para>
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

        var routeKey = httpCtx.GetRouteValue("key") as string;
        if (!IsValidKey(routeKey))
            return Task.CompletedTask;

        var needed = StandardSystemPermissions.Resolve(requirement.Template, routeKey!);

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
