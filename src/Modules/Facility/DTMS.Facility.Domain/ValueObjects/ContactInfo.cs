using DTMS.SharedKernel.Domain;

namespace DTMS.Facility.Domain.ValueObjects;

/// <summary>
/// Primary contact at a warehouse — used by Manual operators to call
/// when arriving / when something's wrong, and by Fleet provider
/// dispatchers to coordinate handover. Name is required because
/// "phone is +66..." with no name is useless when the receiver picks up.
///
/// Phone format is intentionally permissive (no E.164 validation):
/// upstream OMS may send "+66 89-123-4567", "0891234567", or
/// "+66891234567" all for the same number. Storing as-is keeps the
/// display path honest; outbound calls go through a separate service
/// that normalizes.
/// </summary>
public class ContactInfo : ValueObject
{
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }

    private ContactInfo() { }

    public ContactInfo(string name, string phone, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Contact name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Contact phone is required", nameof(phone));

        Name = name.Trim();
        Phone = phone.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Name;
        yield return Phone;
        yield return Email ?? string.Empty;
    }
}
