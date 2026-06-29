namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// One row per <c>(SystemKey, PermissionCode)</c> grant. Parallel to
/// <see cref="RolePermission"/> on the user side — same permission
/// catalog, different principal type. Admin grants/revokes via the
/// admin UI (Phase S.4); enforcement runs through
/// <c>.RequirePermission(...)</c> after the auth middleware identifies
/// the caller as a <c>SystemPrincipal</c>.
/// </summary>
public sealed class SystemClientPermission
{
    public string SystemKey { get; private set; } = string.Empty;
    public string PermissionCode { get; private set; } = string.Empty;
    public DateTime GrantedAt { get; private set; }
    public string? GrantedBy { get; private set; }

    private SystemClientPermission() { }

    public SystemClientPermission(string systemKey, string permissionCode, string? grantedBy = null)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("SystemKey is required.", nameof(systemKey));
        if (string.IsNullOrWhiteSpace(permissionCode))
            throw new ArgumentException("PermissionCode is required.", nameof(permissionCode));

        SystemKey = systemKey;
        PermissionCode = permissionCode;
        GrantedBy = grantedBy;
        GrantedAt = DateTime.UtcNow;
    }
}
