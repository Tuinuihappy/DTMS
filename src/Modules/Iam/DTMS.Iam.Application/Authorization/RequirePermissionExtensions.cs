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
}
