using System.Text.RegularExpressions;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Events;
using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Entities;

/// <summary>
/// One physical carrier — the individual rack or cart goods ride on (ADR-019).
/// Pairs with <see cref="CarrierType"/> the way <see cref="Vehicle"/> pairs with
/// <see cref="VehicleType"/>: a movable asset with a lifecycle, a status, a
/// maintenance history, and a last-known location.
///
/// <para><b>Not a vendor entity.</b> Unlike <see cref="Vehicle"/> there is no
/// AdapterKey or VendorVehicleKey and there never will be — RIOT3 has no concept
/// of a carrier and never reports one. Everything here originates inside DTMS.</para>
///
/// <para><b>Trip binding lives in P3.</b> <see cref="CurrentTripId"/> and the
/// <see cref="CarrierStatus.InUse"/> status have no writer yet; the guards
/// against them are written now so the rules are complete before assignments
/// exist rather than being bolted on afterwards.</para>
/// </summary>
public class Carrier : AggregateRoot<Guid>
{
    // Codes become URL path segments (/carriers/{code}, and the Next.js
    // [code] route). A free-text code containing '/', '#', '%' or a space
    // breaks routing or silently mis-routes, and the person who finds out is
    // an operator, not a test — so the charset is enforced at the domain
    // boundary. This is a transport constraint, not a business format: no
    // pattern like CART-#### is imposed.
    private static readonly Regex CodePattern =
        new(@"^[A-Z0-9][A-Z0-9._-]{0,49}$", RegexOptions.Compiled);

    public string CarrierCode { get; private set; } = string.Empty;
    public Guid CarrierTypeId { get; private set; }

    /// <summary>Scan tag when it differs from <see cref="CarrierCode"/>. Unique
    /// when present; many carriers may have none.</summary>
    public string? Barcode { get; private set; }
    public string? DisplayName { get; private set; }

    public CarrierStatus Status { get; private set; }

    /// <summary>Why it is out of service right now. Cleared on return to service —
    /// the durable record is <see cref="CarrierMaintenanceLog"/>.</summary>
    public string? MaintenanceReason { get; private set; }
    public DateTime? MaintenanceSince { get; private set; }

    /// <summary>Free-text location, meaningful to operators rather than resolved
    /// against stations or WMS locations. P3's auto-release becomes the main
    /// writer; until then admins set it by hand.</summary>
    public string? CurrentLocationCode { get; private set; }

    /// <summary>When <see cref="CurrentLocationCode"/> was last set. A location
    /// with no timestamp invites people to trust stale data, so the two always
    /// move together.</summary>
    public DateTime? LastSeenAt { get; private set; }

    /// <summary>Set by P3 trip binding. Always null in P1.</summary>
    public Guid? CurrentTripId { get; private set; }

    /// <summary>When the carrier physically entered service — distinct from
    /// <see cref="CreatedAt"/> (when the row was made) and backdatable, so a
    /// cart in use since last year can be registered today truthfully.</summary>
    public DateTime? CommissionedAt { get; private set; }

    public DateTime? RetiredAt { get; private set; }
    public string? RetireReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    private Carrier() { }

    public Carrier(
        string carrierCode,
        Guid carrierTypeId,
        string? barcode,
        string? displayName,
        string? currentLocationCode,
        DateTime? commissionedAt,
        string? createdBy)
    {
        Id = Guid.NewGuid();
        CarrierCode = NormalizeAndValidateCode(carrierCode);
        CarrierTypeId = carrierTypeId != Guid.Empty
            ? carrierTypeId
            : throw new ArgumentException("CarrierTypeId must not be empty.", nameof(carrierTypeId));

        Barcode = Trimmed(barcode);
        DisplayName = Trimmed(displayName);
        CommissionedAt = commissionedAt;

        Status = CarrierStatus.Available;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = Trimmed(createdBy);

        if (Trimmed(currentLocationCode) is { } location)
        {
            CurrentLocationCode = location;
            LastSeenAt = CreatedAt;
        }
    }

    /// <summary>
    /// Normalizes to upper-invariant first, then validates — so a lowercase
    /// code is accepted and stored canonically rather than rejected, while
    /// anything outside the URL-safe charset is refused outright.
    /// </summary>
    public static string NormalizeAndValidateCode(string carrierCode)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            throw new ArgumentException("CarrierCode must not be empty.", nameof(carrierCode));

        var normalized = carrierCode.Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(normalized))
            throw new ArgumentException(
                $"CarrierCode '{normalized}' is invalid. Use letters, digits, dot, underscore or hyphen " +
                "(starting with a letter or digit), up to 50 characters.",
                nameof(carrierCode));

        return normalized;
    }

    /// <summary>Editable metadata. The code itself is immutable — see ADR-019:
    /// codes are unique forever so history can never become ambiguous, which a
    /// rename would defeat just as surely as reuse would.</summary>
    public void Update(Guid carrierTypeId, string? barcode, string? displayName,
        DateTime? commissionedAt, string? modifiedBy)
    {
        if (Status == CarrierStatus.InUse)
            throw new InvalidOperationException("Cannot edit a carrier while it is in use.");
        if (carrierTypeId == Guid.Empty)
            throw new ArgumentException("CarrierTypeId must not be empty.", nameof(carrierTypeId));

        CarrierTypeId = carrierTypeId;
        Barcode = Trimmed(barcode);
        DisplayName = Trimmed(displayName);
        CommissionedAt = commissionedAt;
        Touch(modifiedBy);
    }

    /// <summary>Allowed in every status: a cart under repair or already retired
    /// still physically moves — to the workshop, to the scrap yard — and
    /// recording that is the whole point of tracking location.</summary>
    public void MoveTo(string? locationCode, string? modifiedBy)
    {
        CurrentLocationCode = Trimmed(locationCode);
        LastSeenAt = DateTime.UtcNow;
        Touch(modifiedBy);
    }

    public void EnterMaintenance(string reason, string? modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Maintenance reason is required.", nameof(reason));
        if (Status is CarrierStatus.InUse)
            throw new InvalidOperationException("Cannot service a carrier while it is in use.");
        if (Status is CarrierStatus.Retired)
            throw new InvalidOperationException("Cannot service a retired carrier. Un-retire it first.");
        if (Status is CarrierStatus.Maintenance)
            throw new InvalidOperationException("Carrier is already under maintenance.");

        Transition(CarrierStatus.Maintenance, reason.Trim(), modifiedBy);
        MaintenanceReason = reason.Trim();
        MaintenanceSince = DateTime.UtcNow;
    }

    public void ReturnToService(string? modifiedBy)
    {
        if (Status is not CarrierStatus.Maintenance)
            throw new InvalidOperationException("Only a carrier under maintenance can return to service.");

        Transition(CarrierStatus.Available, null, modifiedBy);
        MaintenanceReason = null;
        MaintenanceSince = null;
    }

    /// <summary>End of life. The row and its history stay, and the code stays
    /// reserved forever. Reversible via <see cref="Unretire"/> so a mis-click
    /// is not a permanent dead row.</summary>
    public void Retire(string reason, string? modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Retire reason is required.", nameof(reason));
        if (Status is CarrierStatus.InUse)
            throw new InvalidOperationException("Cannot retire a carrier while it is in use.");
        if (Status is CarrierStatus.Retired)
            throw new InvalidOperationException("Carrier is already retired.");

        Transition(CarrierStatus.Retired, reason.Trim(), modifiedBy);
        RetiredAt = DateTime.UtcNow;
        RetireReason = reason.Trim();
        // A carrier retired mid-repair leaves no dangling maintenance state; the
        // open log row is closed by the command handler in the same save.
        MaintenanceReason = null;
        MaintenanceSince = null;
    }

    /// <summary>
    /// Undo a retirement. Returns to Available rather than to whatever it was
    /// before — if it still needs repair, send it to maintenance again, which
    /// records a truthful new episode instead of pretending the closed one never
    /// ended. Does not free the code: this is the same row coming back, so
    /// history stays attached and unambiguous.
    /// </summary>
    public void Unretire(string? modifiedBy)
    {
        if (Status is not CarrierStatus.Retired)
            throw new InvalidOperationException("Only a retired carrier can be un-retired.");

        Transition(CarrierStatus.Available, null, modifiedBy);
        RetiredAt = null;
        RetireReason = null;
    }

    /// <summary>
    /// Whether the row may be destroyed outright. Deletion exists for carriers
    /// that should never have existed, so it is gated on having no history to
    /// destroy — which is also what keeps it from becoming a back door around
    /// the forever-unique code rule: anything with history cannot be deleted, so
    /// no history can ever be made ambiguous by a code being freed.
    ///
    /// <para><paramref name="maintenanceLogCount"/> is passed in because the log
    /// is a sibling entity, not a child collection on this aggregate. P3 adds an
    /// assignment count alongside it; the shape is built for that extra clause.</para>
    /// </summary>
    public (bool CanDelete, string? Reason) CanDelete(int maintenanceLogCount)
    {
        if (Status is not CarrierStatus.Available)
            return (false, $"Carrier is {Status}. Only an available carrier can be deleted.");
        if (maintenanceLogCount > 0)
            return (false,
                $"Carrier has {maintenanceLogCount} maintenance record(s). Retire it instead so the history is kept.");

        return (true, null);
    }

    private void Transition(CarrierStatus newStatus, string? reason, string? modifiedBy)
    {
        var oldStatus = Status;
        Status = newStatus;
        Touch(modifiedBy);
        AddDomainEvent(new CarrierStatusChangedDomainEvent(
            Id, CarrierCode, oldStatus, newStatus, reason));
    }

    private void Touch(string? modifiedBy)
    {
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = Trimmed(modifiedBy);
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
