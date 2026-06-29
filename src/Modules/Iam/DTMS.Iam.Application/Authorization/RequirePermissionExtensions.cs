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

    /// <summary>
    /// Phase S.2 variant for federated source-system endpoints. Does
    /// NOT pin the policy to the Bearer scheme — the policy trusts the
    /// <c>HttpContext.User</c> that <see cref="DTMS.Api.Middlewares"/>
    /// <c>SystemClientAuthMiddleware</c> set, including its
    /// already-stamped permission claims. Apply only to endpoints
    /// mounted under <c>/api/v1/source/{key}/*</c>; using it on a
    /// user-facing endpoint would bypass the Bearer scheme guard.
    /// </summary>
    public static TBuilder RequirePermissionForSourceSystem<TBuilder>(
        this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
    }

    /// <summary>
    /// Phase S.3.1a — route-aware variant. The <paramref name="template"/>
    /// carries the literal placeholder <c>{key}</c>, resolved at
    /// authorization time from <c>HttpContext.GetRouteValue("key")</c>.
    /// Lets a single endpoint registration guard every system slug —
    /// admin onboards a new SystemClient in DB and the same endpoint
    /// authorises it without redeploy.
    ///
    /// <para>Pair with <see cref="SourceSystemPermissionHandler"/>; both
    /// must be registered in DI. Apply only under
    /// <c>/api/v1/source/{key}/*</c> for the same Bearer-scheme reason
    /// noted on <see cref="RequirePermissionForSourceSystem"/>.</para>
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
