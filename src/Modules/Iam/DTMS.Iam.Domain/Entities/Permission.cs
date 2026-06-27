namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// A single capability the system can grant. Codes follow the convention
/// <c>{service}:{resource}:{action}</c> (e.g. <c>dtms:facility:map:import</c>).
/// The leading service prefix ("dtms:") is mandatory now so we don't have
/// to rename rows when sibling services adopt their own namespace.
/// </summary>
public sealed class Permission
{
    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    private Permission() { }

    public Permission(string code, string description, string module)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Permission code is required.", nameof(code));
        if (!code.StartsWith("dtms:", StringComparison.Ordinal))
            throw new ArgumentException(
                "Permission code must start with 'dtms:' prefix.", nameof(code));

        Code = code;
        Description = description ?? string.Empty;
        Module = module ?? string.Empty;
        CreatedAt = DateTime.UtcNow;
    }
}
