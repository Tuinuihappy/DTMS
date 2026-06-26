namespace DTMS.Transport.Manual.Application.Services;

// Phase 4.2 — Haversine distance (great-circle, WGS84). Pure function
// used by the pickup/drop endpoint handlers to decide whether the
// operator's reported GPS coordinate falls within Warehouse.GeofenceRadiusM
// (per ADR-016 server-strict enforcement).
//
// Polygon geofences (Warehouse.GeofenceAreaWkt) are deferred — NTS-based
// point-in-polygon check would land in Phase 4.4 alongside the real
// dispatch strategy. For now we honour radius geofences only; warehouses
// with WKT-only geofences fall through to "no enforcement" (matches
// current Facility seed data where radius is always set).
public static class GeofenceCalculator
{
    private const double EarthRadiusMeters = 6_371_000d;

    public static double DistanceMeters(
        double lat1, double lng1, double lat2, double lng2)
    {
        var lat1Rad = ToRadians(lat1);
        var lat2Rad = ToRadians(lat2);
        var deltaLat = ToRadians(lat2 - lat1);
        var deltaLng = ToRadians(lng2 - lng1);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
              + Math.Cos(lat1Rad) * Math.Cos(lat2Rad)
              * Math.Sin(deltaLng / 2) * Math.Sin(deltaLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    public record GeofenceCheckResult(bool IsInside, double DistanceM, int? RadiusM)
    {
        public double OvershootM => IsInside ? 0 : DistanceM - (RadiusM ?? 0);
    }

    public static GeofenceCheckResult Check(
        double reportedLat, double reportedLng,
        double warehouseLat, double warehouseLng,
        int? radiusM)
    {
        // No radius set on the warehouse = no enforcement (treat as
        // inside). Matches Fleet customer-address case from ADR-016.
        if (!radiusM.HasValue || radiusM.Value <= 0)
            return new GeofenceCheckResult(IsInside: true, DistanceM: 0, RadiusM: null);

        var distance = DistanceMeters(reportedLat, reportedLng, warehouseLat, warehouseLng);
        return new GeofenceCheckResult(
            IsInside: distance <= radiusM.Value,
            DistanceM: distance,
            RadiusM: radiusM);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
