using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.ValueObjects;

/// <summary>
/// Postal address for a warehouse or delivery destination. Free-form
/// strings (no enum / lookup) because Thailand addresses vary widely
/// and locking to a strict schema would force ingest layers to munge
/// upstream OMS data. The Address is for human display + 3PL handover —
/// geofence checks use <see cref="LatLng"/> instead.
///
/// Only Street is required; the rest are optional because some
/// warehouses are described purely by GPS + free-text (industrial parks
/// without postal codes, customer pickups at construction sites).
/// </summary>
public class Address : ValueObject
{
    public string Street { get; private set; } = string.Empty;
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string? Country { get; private set; }

    private Address() { }

    public Address(string street, string? city = null, string? state = null,
                   string? postalCode = null, string? country = null)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street is required", nameof(street));

        Street = street.Trim();
        City = NormalizeOptional(city);
        State = NormalizeOptional(state);
        PostalCode = NormalizeOptional(postalCode);
        Country = NormalizeOptional(country);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City ?? string.Empty;
        yield return State ?? string.Empty;
        yield return PostalCode ?? string.Empty;
        yield return Country ?? string.Empty;
    }

    public override string ToString()
    {
        var parts = new[] { Street, City, State, PostalCode, Country }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }
}
