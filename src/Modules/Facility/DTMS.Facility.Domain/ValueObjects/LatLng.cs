using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.ValueObjects;

/// <summary>
/// WGS84 geographic coordinate (decimal degrees). Used for warehouse
/// addresses, operator GPS positions, and 3PL delivery destinations —
/// anything that needs a real-world point. Distinct from
/// <see cref="Coordinate"/> which is factory-local (RIOT3 map frame).
///
/// Validation matches reality, not the wider float range: latitude is
/// the bounded axis (-90 → 90), longitude wraps (-180 → 180). Throwing
/// on invalid construction lets ingest layers fail at the boundary
/// instead of silently storing nonsense like (190, 200).
/// </summary>
public class LatLng : ValueObject
{
    public double Lat { get; private set; }
    public double Lng { get; private set; }

    private LatLng() { }

    public LatLng(double lat, double lng)
    {
        if (lat < -90 || lat > 90)
            throw new ArgumentOutOfRangeException(nameof(lat),
                $"Latitude must be in [-90, 90], got {lat}");
        if (lng < -180 || lng > 180)
            throw new ArgumentOutOfRangeException(nameof(lng),
                $"Longitude must be in [-180, 180], got {lng}");

        Lat = lat;
        Lng = lng;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Lat;
        yield return Lng;
    }

    public override string ToString() => $"({Lat:F6}, {Lng:F6})";
}
