namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// Append-only audit row capturing a single IAM administrative action.
/// Written by the application layer whenever an admin grants/revokes a
/// role permission, creates/updates/deletes a permission, or manages a
/// role. The frontend audit log view + any future compliance export
/// reads from this table.
/// </summary>
public sealed class PermissionAuditEntry
{
    public Guid Id { get; private set; }
    public DateTime OccurredAt { get; private set; }

    /// <summary>EmployeeId of the admin who performed the action.</summary>
    public string ActorEmployeeId { get; private set; } = string.Empty;

    /// <summary>One of: grant, revoke, permission-created, permission-updated,
    /// permission-deleted, role-created, role-updated, role-deleted.</summary>
    public string Action { get; private set; } = string.Empty;

    public string? Role { get; private set; }
    public string? PermissionCode { get; private set; }

    /// <summary>Free-text JSON snapshot when relevant (e.g. previous
    /// permission description before an update). Null for simple
    /// grant/revoke rows where the (Role, PermissionCode) pair is enough.</summary>
    public string? Details { get; private set; }

    private PermissionAuditEntry() { }

    public PermissionAuditEntry(
        string actorEmployeeId,
        string action,
        string? role = null,
        string? permissionCode = null,
        string? details = null)
    {
        if (string.IsNullOrWhiteSpace(actorEmployeeId))
            throw new ArgumentException("Actor EmployeeId is required.", nameof(actorEmployeeId));
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));

        Id = Guid.NewGuid();
        OccurredAt = DateTime.UtcNow;
        ActorEmployeeId = actorEmployeeId;
        Action = action;
        Role = role;
        PermissionCode = permissionCode;
        Details = details;
    }
}
