using System.Text.RegularExpressions;
using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Entities;

/// <summary>
/// A class of carrier — the rack, cart, or trolley kind that goods ride on
/// (ADR-019). Pairs with <see cref="Carrier"/> the way
/// <see cref="VehicleType"/> pairs with <see cref="Vehicle"/>.
///
/// <para>Moved here from Facility in carrier-tracking P0.2: Facility models
/// static site topology (maps, stations), while a carrier is a movable asset
/// with a lifecycle — Fleet's domain. The table kept its rows through an
/// <c>ALTER TABLE ... SET SCHEMA</c>; see migration 20260827100001.</para>
///
/// <para><b>Not a vendor entity.</b> Unlike <see cref="Vehicle"/> this has no
/// AdapterKey or VendorVehicleKey and never will — RIOT3 has no concept of a
/// carrier and never reports one.</para>
/// </summary>
public class CarrierType : Entity<Guid>
{
    // Same charset, and for the same reason, as Carrier.CodePattern: the code is
    // a URL path segment on /carrier-types/{code} and the Next.js [code] route,
    // so '/', '#', '%' or a space would break or silently mis-route it.
    private static readonly Regex CodePattern =
        new(@"^[A-Z0-9][A-Z0-9._-]{0,49}$", RegexOptions.Compiled);

    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AmrCapability { get; private set; } = string.Empty;
    public double? MaxWeightKg { get; private set; }
    public int? MaxSlots { get; private set; }
    public string? Description { get; private set; }

    private CarrierType() { }

    public CarrierType(string code, string displayName,
        string amrCapability, double? maxWeightKg = null,
        int? maxSlots = null, string? description = null)
    {
        Id = Guid.NewGuid();
        Code = NormalizeAndValidateCode(code);
        Apply(displayName, amrCapability, maxWeightKg, maxSlots, description);
    }

    /// <summary>
    /// Normalizes to upper-invariant first, then validates — so a lowercase code
    /// is accepted and stored canonically rather than rejected, while anything
    /// outside the URL-safe charset is refused outright.
    /// </summary>
    public static string NormalizeAndValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code must not be empty.", nameof(code));

        var normalized = code.Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(normalized))
            throw new ArgumentException(
                $"Code '{normalized}' is invalid. Use letters, digits, dot, underscore or hyphen " +
                "(starting with a letter or digit), up to 50 characters.",
                nameof(code));

        return normalized;
    }

    /// <summary>Editable metadata. <see cref="Code"/> is absent on purpose: it is
    /// how every carrier payload names its type and how this row is addressed, so
    /// a rename would silently invalidate callers holding the old one.</summary>
    public void Update(string displayName, string amrCapability,
        double? maxWeightKg, int? maxSlots, string? description)
        => Apply(displayName, amrCapability, maxWeightKg, maxSlots, description);

    private void Apply(string displayName, string amrCapability,
        double? maxWeightKg, int? maxSlots, string? description)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(amrCapability))
            throw new ArgumentException("AmrCapability must not be empty.", nameof(amrCapability));
        if (maxWeightKg is <= 0)
            throw new ArgumentException("MaxWeightKg must be greater than zero.", nameof(maxWeightKg));
        if (maxSlots is <= 0)
            throw new ArgumentException("MaxSlots must be greater than zero.", nameof(maxSlots));

        DisplayName = displayName.Trim();
        AmrCapability = amrCapability.Trim();
        MaxWeightKg = maxWeightKg;
        MaxSlots = maxSlots;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>
    /// Whether the row may be destroyed outright. A type in use is refused here
    /// rather than at the database: Carriers.CarrierTypeId is ON DELETE RESTRICT,
    /// so without this the caller would get a raw 23503 scrubbed into a 500
    /// instead of being told how many carriers are holding the type.
    ///
    /// <para><paramref name="carriersUsing"/> is passed in because Carrier is a
    /// sibling aggregate, not a child collection — the same shape as
    /// <see cref="Carrier.CanDelete"/>.</para>
    /// </summary>
    public (bool CanDelete, string? Reason) CanDelete(int carriersUsing)
        => carriersUsing > 0
            ? (false, $"Carrier type '{Code}' is used by {carriersUsing} carrier(s). " +
                      "Move or delete them first.")
            : (true, null);
}
