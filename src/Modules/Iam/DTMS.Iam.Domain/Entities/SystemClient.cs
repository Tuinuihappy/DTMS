namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// An external source system that DTMS recognises as a first-class
/// caller — e.g. <c>oms</c>, <c>sap</c>, <c>wms-acme</c>. The
/// <see cref="Key"/> is the canonical short slug used in routes
/// (<c>/api/v1/source/*</c>), audit logs, and config maps.
/// Lives alongside the user-role world but never shares a table —
/// lifecycles, credentials, and admin workflows diverge enough that
/// unifying them in storage would force coincidence-only joins.
/// </summary>
public sealed class SystemClient
{
    public string Key { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public string? OwnerContact { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SystemClient() { }

    public SystemClient(
        string key,
        string displayName,
        string? description = null,
        string? ownerContact = null,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));
        if (key.Length > 50)
            throw new ArgumentException("Key must be 50 characters or fewer.", nameof(key));
        // Phase S.3.1a tightened this from "no path-breaking chars" to
        // a strict slug: the key gets interpolated into permission codes
        // (dtms:source:{key}:order:write) at endpoint enforcement time,
        // so any character that could land in those codes must be
        // alphanumeric-and-dash or admin could craft a Key that grants
        // unintended access (e.g. 'oms:*').
        foreach (var c in key)
        {
            var ok = (c >= 'a' && c <= 'z')
                  || (c >= '0' && c <= '9')
                  || c == '-';
            if (!ok)
                throw new ArgumentException(
                    "Key must contain only lowercase letters, digits, and '-' (no underscores, dots, colons, uppercase, or spaces).",
                    nameof(key));
        }
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required.", nameof(displayName));

        Key = key;
        DisplayName = displayName;
        Description = description;
        OwnerContact = ownerContact;
        IsActive = isActive;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        DisplayName = displayName;
    }

    public void UpdateDescription(string? description) => Description = description;
    public void UpdateOwnerContact(string? ownerContact) => OwnerContact = ownerContact;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
