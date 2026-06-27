using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Evaluates <see cref="PermissionRequirement"/> against the claims
/// stamped by <see cref="PermissionClaimsTransformer"/>. A request
/// passes if the caller holds either the exact permission code or
/// any matching wildcard (e.g. <c>dtms:*</c> grants every dtms perm,
/// <c>dtms:facility:*</c> grants every Facility perm).
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var held = context.User.FindAll(PermissionClaimsTransformer.PermissionClaimType);
        foreach (var claim in held)
        {
            if (Matches(claim.Value, requirement.Permission))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }
        return Task.CompletedTask;
    }

    // "dtms:*" matches "dtms:facility:map:import".
    // "dtms:facility:*" matches "dtms:facility:map:import" but not "dtms:planning:*".
    // No-wildcard codes must match exactly.
    private static bool Matches(string held, string required)
    {
        if (held == required) return true;
        if (!held.EndsWith(":*", StringComparison.Ordinal)) return false;

        var prefix = held[..^1]; // keep the trailing ':'
        return required.StartsWith(prefix, StringComparison.Ordinal);
    }
}
