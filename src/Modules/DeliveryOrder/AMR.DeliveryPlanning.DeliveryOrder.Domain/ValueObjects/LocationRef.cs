using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Discriminated reference to a station — either by human-readable Code (e.g. "SHELF-05")
/// or by Station Guid. Exactly one of the two is non-null. The original form is preserved
/// so that <c>GET /orders</c> returns the same shape the caller sent on <c>POST</c>.
/// </summary>
public sealed class LocationRef : ValueObject
{
    public string? Code { get; private set; }
    public Guid? StationId { get; private set; }

    public bool IsCode => Code is not null;
    public bool IsStationId => StationId.HasValue;

    private LocationRef() { } // EF

    private LocationRef(string? code, Guid? stationId)
    {
        Code = code;
        StationId = stationId;
    }

    public static LocationRef FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code must not be empty.", nameof(code));
        return new LocationRef(code.Trim(), null);
    }

    public static LocationRef FromStationId(Guid stationId)
    {
        if (stationId == Guid.Empty)
            throw new ArgumentException("StationId must not be Guid.Empty.", nameof(stationId));
        return new LocationRef(null, stationId);
    }

    public override string ToString() =>
        Code ?? (StationId.HasValue ? StationId.Value.ToString() : "<empty>");

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Code ?? string.Empty;
        yield return StationId ?? Guid.Empty;
    }
}
