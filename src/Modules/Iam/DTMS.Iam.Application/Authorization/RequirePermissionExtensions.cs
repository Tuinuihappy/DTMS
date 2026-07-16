using Microsoft.AspNetCore.Builder;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Endpoint-level shortcut for permission-based authorization. Replaces
/// the role-based <c>.RequireAuthorization(OperatorOnlyPolicy)</c> style
/// with finer-grained <c>.RequirePermission("dtms:facility:map:import")</c>.
/// </summary>
public static class RequirePermissionExtensions
{
    // Hardcoded to avoid taking a heavyweight dependency on
    // Microsoft.AspNetCore.Authentication.JwtBearer here — the value is
    // a stable constant ("Bearer"). The api project owns the actual
    // scheme registration in Program.cs.
    private const string BearerScheme = "Bearer";

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(BearerScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
    }

    // Phase 5 — RequirePermissionForSourceSystem was removed. It took a
    // LITERAL permission, so its only caller (source/whoami) pinned
    // `dtms:source:oms:order:read` and 403'd every non-OMS system. Source
    // endpoints that need a grant use RequirePermissionFromRouteKey below,
    // which resolves {key} to the caller's own system.

    /// <summary>
    /// Phase S.3.1a — caller-aware variant. The <paramref name="template"/>
    /// carries the literal placeholder <c>{key}</c>, resolved at
    /// authorization time to the CALLER's own system key (taken from the
    /// <c>SystemPrincipal</c> that SystemClientAuthMiddleware stamped on
    /// <c>HttpContext.Items</c> — despite the name, no route value is read;
    /// the slug was dropped from these URLs in S.8e). Lets a single endpoint
    /// registration guard every system — admin onboards a new SystemClient
    /// in DB and the same endpoint authorises it without redeploy.
    ///
    /// <para>Pair with <see cref="SourceSystemPermissionHandler"/>; both
    /// must be registered in DI. Apply only under <c>/api/v1/source/*</c>:
    /// the policy does NOT pin the Bearer scheme, because it trusts the
    /// <c>HttpContext.User</c> that SystemClientAuthMiddleware built
    /// (permission claims already stamped). On a user-facing endpoint that
    /// would bypass the Bearer scheme guard.</para>
    /// </summary>
    public static TBuilder RequirePermissionFromRouteKey<TBuilder>(
        this TBuilder builder, string template)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new SourceSystemPermissionRequirement(template)));
    }
}
