namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// Many-to-many mapping between External Auth roles (e.g. "Admin",
/// "Supervisor", "Operator") and DTMS permissions. A role gains every
/// permission listed against its name.
///
/// Wildcard codes (e.g. "dtms:*") are honoured by the authorization
/// handler at request time — they don't need to be exploded into one
/// row per permission here.
/// </summary>
public sealed class RolePermission
{
    public string Role { get; private set; } = string.Empty;
    public string PermissionCode { get; private set; } = string.Empty;

    private RolePermission() { }

    public RolePermission(string role, string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role is required.", nameof(role));
        if (string.IsNullOrWhiteSpace(permissionCode))
            throw new ArgumentException("Permission code is required.", nameof(permissionCode));

        Role = role;
        PermissionCode = permissionCode;
    }
}
