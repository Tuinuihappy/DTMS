namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// A role name that DTMS recognises in incoming JWTs. The set of valid
/// roles is admin-managed at DTMS, but actual role assignment to users
/// happens at External Auth — creating a new row here only matters once
/// External Auth starts issuing tokens with that role name. The system
/// roles (Admin/Supervisor/Operator) are seeded by migration and cannot
/// be deleted via the API.
/// </summary>
public sealed class Role
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Role() { }

    public Role(string name, string description, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required.", nameof(name));
        if (name.Length > 50)
            throw new ArgumentException("Role name must be 50 characters or fewer.", nameof(name));

        Name = name;
        Description = description ?? string.Empty;
        IsSystem = isSystem;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string description)
    {
        Description = description ?? string.Empty;
    }
}
