using Microsoft.AspNetCore.Authorization;

namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Authorization requirement asking the caller to hold a specific
/// permission code (e.g. <c>dtms:facility:map:import</c>). Paired with
/// <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission is required.", nameof(permission));
        Permission = permission;
    }
}
