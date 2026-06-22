# ADR-010: Geofence Implementation (PostGIS vs In-Memory)

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [ADR-002](adr-002-facility-station-hierarchy.md), [Phase 4](../phases/phase-4-transport-manual.md), [Manual Operator API](../api/manual-operator-api.md)

## Context

Phase 4 (Manual mode) ต้องการ **geofence verification** — เมื่อ operator capture pickup/drop ต้องตรวจสอบว่า GPS coord อยู่ใน warehouse boundary ที่กำหนด

ตาม [ADR-002](adr-002-facility-station-hierarchy.md), `Facility` (Warehouse) มี geofence 2 รูปแบบ:
- **Circle**: `GeofenceRadiusM` (meters from center point)
- **Polygon**: `GeofenceArea` (arbitrary shape — สำหรับ warehouse ใหญ่)

ปัญหาที่ต้องตัดสิน:
1. **เก็บ polygon ใน DB ยังไง?** GeoJSON string, WKT (well-known text), หรือ native spatial type (PostGIS geometry)
2. **คำนวณ "GPS in geofence?" ที่ไหน?** Application code (C#) หรือ database (SQL)
3. **ต้องการ PostGIS extension หรือไม่?** PostGIS = additional Postgres extension (เพิ่ม operational complexity)
4. **Library choice ใน .NET** — NetTopologySuite, custom haversine, etc.
5. **Performance budget**: เรียกระหว่าง mobile API request — ต้อง < 50ms (cheap operation)
6. **Local dev / CI** — PostGIS available in Postgres image? อยากให้ setup ง่าย

Existing infra (จากตรวจ):
- Postgres 16 (CI: `postgres:16` Docker image — **no PostGIS by default**)
- ไม่มี spatial query patterns ใน codebase ปัจจุบัน
- Operator app sends `{ lat, lng }` ทุก pickup/drop call

## Decision

ใช้ **NetTopologySuite (NTS) in-memory calculation** สำหรับ Phase 4 launch + **PostGIS deferred** (พิจารณาใหม่ใน Phase 5+ ถ้ามี spatial queries อื่น)

### Reasoning Summary

| Criterion | In-Memory (NTS) | PostGIS |
|---|---|---|
| Operational complexity | Low (just C# library) | Medium (extension + permissions) |
| Local dev parity | Standard Postgres image | Custom postgis/postgis image |
| Performance @ scale | Fine for our pattern (1-row check per API call) | Better for batch / proximity queries |
| Spatial index needed? | No (we look up Facility by ID first) | Yes for "find nearby" — not our pattern |
| Library quality | NTS is mature .NET port of JTS | PostGIS is gold-standard but C/SQL |
| Future flexibility | Migration to PostGIS easy if needed | Lock-in if used heavily |

Our usage pattern: "given Facility ID and GPS, is GPS in geofence?" → 1 row, 1 calculation. PostGIS overkill.

### Implementation Decision Details

#### 1. Polygon Storage Format: WKT (Well-Known Text)

```csharp
public class Facility
{
    public int? GeofenceRadiusM { get; private set; }
    public string? GeofenceAreaWkt { get; private set; }   // "POLYGON((100.5 13.7, 100.51 13.7, ...))"

    public bool IsInsideGeofence(LatLng point, IGeometryParser parser)
    {
        if (GeofenceAreaWkt is not null)
        {
            var polygon = parser.ParsePolygon(GeofenceAreaWkt);
            return polygon.Contains(parser.MakePoint(point.Lng, point.Lat));
        }

        if (GeofenceRadiusM is { } radius)
        {
            return HaversineDistance(Location, point) <= radius;
        }

        return true;   // no geofence = no enforcement
    }
}
```

**Why WKT:**
- Plain text — debuggable in SQL queries
- Standard format — readable by PostGIS later if migrated
- Storable in `varchar` (no Postgres extension needed)
- NetTopologySuite reads WKT natively

**Schema:**
```sql
CREATE TABLE facility.warehouses (
    -- ... other columns
    geofence_radius_m INTEGER,                   -- nullable
    geofence_area_wkt VARCHAR(5000),             -- nullable; mutually exclusive with radius
    location_lat DOUBLE PRECISION NOT NULL,
    location_lng DOUBLE PRECISION NOT NULL
);

ALTER TABLE facility.warehouses
ADD CONSTRAINT chk_geofence_exclusive
CHECK (geofence_radius_m IS NULL OR geofence_area_wkt IS NULL);
```

#### 2. Calculation: Server-Side in Application

```csharp
// in Transport.Manual.Application — geofence check during pickup/drop
public sealed class GeofenceValidator : IGeofenceValidator
{
    private readonly WKTReader _wktReader;
    private readonly GeometryFactory _geometryFactory;

    public GeofenceValidator()
    {
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);  // WGS84 SRID
        _wktReader = new WKTReader(_geometryFactory);
    }

    public GeofenceCheckResult Validate(LatLng point, Facility facility)
    {
        // Polygon check (if defined)
        if (!string.IsNullOrEmpty(facility.GeofenceAreaWkt))
        {
            var polygon = (Polygon)_wktReader.Read(facility.GeofenceAreaWkt);
            var ntsPoint = _geometryFactory.CreatePoint(new Coordinate(point.Lng, point.Lat));
            var inside = polygon.Contains(ntsPoint);
            return new GeofenceCheckResult(inside, distanceM: null);
        }

        // Circle check
        if (facility.GeofenceRadiusM is { } radius)
        {
            var distance = Haversine.DistanceMeters(facility.Location, point);
            return new GeofenceCheckResult(distance <= radius, distance);
        }

        // No geofence configured
        return new GeofenceCheckResult(IsInside: true, distanceM: null);
    }
}

public sealed record GeofenceCheckResult(bool IsInside, double? DistanceM);
```

#### 3. Haversine Distance Helper

```csharp
public static class Haversine
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(LatLng a, LatLng b)
    {
        var lat1 = ToRadians(a.Lat);
        var lat2 = ToRadians(b.Lat);
        var dLat = ToRadians(b.Lat - a.Lat);
        var dLng = ToRadians(b.Lng - a.Lng);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
```

**Accuracy:** Haversine assumes spherical earth — error < 0.5% over distances < 100km. ใช้สำหรับ geofence ขนาด < 10km accuracy = ~50m error ที่ worst case → acceptable

#### 4. Polygon Drawing UI (Frontend)

For dispatcher to define complex polygons:

```tsx
// frontend/components/facility/geofence-editor.tsx
import { MapContainer, FeatureGroup, Polygon } from 'react-leaflet';
import { EditControl } from 'react-leaflet-draw';

export function GeofenceEditor({ facility, onChange }) {
  return (
    <MapContainer center={[facility.lat, facility.lng]} zoom={17}>
      <FeatureGroup>
        <EditControl
          position="topright"
          onEdited={(e) => {
            const layer = e.layers.getLayers()[0];
            const latlngs = layer.getLatLngs()[0];
            const wkt = `POLYGON((${latlngs.map(p => `${p.lng} ${p.lat}`).join(', ')}, ${latlngs[0].lng} ${latlngs[0].lat}))`;
            onChange({ ...facility, geofenceAreaWkt: wkt, geofenceRadiusM: null });
          }}
          draw={{ rectangle: false, circle: true, polygon: true, marker: false }}
        />
      </FeatureGroup>
    </MapContainer>
  );
}
```

Library: `react-leaflet` + `react-leaflet-draw` (existing patterns in [facility map components](../../../frontend/components/facility/))

## Alternatives Considered

### Alternative A: PostGIS for Storage + Calculation

Install PostGIS extension; store geometry in native `geometry(POLYGON, 4326)` column; check in SQL

**Pros:**
- Native spatial indexing (GIST)
- Industry-standard for spatial queries
- Best performance for batch queries
- Built-in distance + intersection operators
- Future-proof สำหรับ complex spatial queries (find nearby warehouses, region analysis)

**Cons:**
- Postgres extension: needs DBA approval, custom image (`postgis/postgis:16`)
- Local dev: ทุกคนต้องเปลี่ยน Docker image
- Per memory + CI workflow currently uses `postgres:16` — change affects all 17 test projects
- For 1-row geofence check per API call: no measurable performance gain
- Lock-in: WKT migration easy → PostGIS, but PostGIS columns harder to extract

**Rejected for Phase 4:** Operational cost > benefit for our query pattern. Re-evaluate ใน Phase 5+ ถ้ามี spatial use case อื่น (เช่น "หา warehouse ใกล้สุด" สำหรับ Fleet provider selection)

### Alternative B: In-DB Calculation (no PostGIS) using Trigonometry SQL

Use Postgres native math functions:

```sql
SELECT
  6371000 * acos(
    cos(radians(:lat1)) * cos(radians(:lat2)) *
    cos(radians(:lng2) - radians(:lng1)) +
    sin(radians(:lat1)) * sin(radians(:lat2))
  ) AS distance_m
FROM facility.warehouses
WHERE id = :warehouseId;
```

**Pros:**
- No client-side library
- Calculation co-located with data

**Cons:**
- Only handles circles, not polygons
- Polygon contains check = nightmare in plain SQL
- More complex code path (SQL string assembly vs object method)

**Rejected:** insufficient for polygons; not enough benefit for circles

### Alternative C: Cloud Service (Google Maps Geocoding / Mapbox Geofencing)

Use external API

**Pros:**
- Offload calculation
- Geo intelligence built-in (e.g., understand "Warehouse District")

**Cons:**
- External dependency for **every API call** (operator pickup)
- Latency + cost
- Privacy: GPS coords sent to 3rd party
- Overkill for our needs

**Rejected:** unnecessary external dependency

### Alternative D: Custom Pure-C# Implementation (no library)

Implement haversine + ray-casting algorithm for polygon contains

**Pros:**
- Zero dependencies
- Total control

**Cons:**
- Reinvent wheel — NTS battle-tested
- Edge cases (longitude wraparound, polygon self-intersection) easy to miss
- Maintenance burden

**Rejected:** NTS is small library, well-maintained

### Alternative E: GeoJSON Format Instead of WKT

Store polygons as GeoJSON in `jsonb` column

**Pros:**
- Native to web (Leaflet uses GeoJSON)
- Postgres `jsonb` queryable

**Cons:**
- Larger size than WKT (more verbose)
- WKT is OGC standard for spatial — better future PostGIS migration
- Same parsing complexity in C#

**Considered but Rejected:** WKT slightly better for future-proofing. Either acceptable; WKT chosen for consistency with OGC.

## Implementation Details

### NuGet Dependencies

```xml
<!-- in Transport.Manual.Application/.csproj -->
<PackageReference Include="NetTopologySuite" Version="2.5.0" />
```

NTS = small library (~600KB), no native dependencies, ASLv2 license

### DI Registration

```csharp
// in TransportManualServiceCollectionExtensions
services.AddSingleton<IGeofenceValidator, GeofenceValidator>();   // stateless, thread-safe
```

### Endpoint Integration

```csharp
// in OperatorEndpoints.cs (Capture pickup)
private static async Task<IResult> CapturePickup(
    Guid tripId,
    [FromBody] CapturePickupRequest req,
    IGeofenceValidator validator,
    IFacilityLookup facilities,
    ITripRepository trips,
    IManualTripExtensionRepository extRepo,
    ICurrentOperator currentOp,
    ManualModeOptions options,    // injected
    CancellationToken ct)
{
    var trip = await trips.GetByIdAsync(tripId, ct);
    var facility = await facilities.GetAsync(trip.PickupFacilityId, ct);

    // Conditional geofence check (per ADR-006 feature flag)
    if (options.Features.GeofenceEnforcement)
    {
        var check = validator.Validate(req.GpsCoord, facility);
        if (!check.IsInside)
        {
            return Results.BadRequest(new {
                error = "GeofenceViolation",
                message = $"GPS {req.GpsCoord} outside geofence",
                warehouseLocation = facility.Location,
                providedLocation = req.GpsCoord,
                distanceM = check.DistanceM,
                allowedM = facility.GeofenceRadiusM
            });
        }
    }

    // ... record pickup
}
```

### Performance

Benchmark expectation per call:
- Polygon contains check (10-vertex polygon): ~5μs
- Haversine distance: ~1μs
- WKT parse: ~50μs (cache parsed polygon if performance critical)

For 100 concurrent operator requests: <500ms total CPU = negligible

**Optimization (if needed later):** Cache parsed Polygon objects per FacilityId — invalidate on update

### Testing Strategy

```csharp
// Unit tests
[Theory]
[InlineData(13.7, 100.5, 13.701, 100.501, true)]      // inside ~150m radius (Bangkok)
[InlineData(13.7, 100.5, 13.71, 100.51, false)]       // outside ~1.4km
public void GeofenceValidator_Circle(double cLat, double cLng, double pLat, double pLng, bool expected)
{
    var facility = new FacilityBuilder()
        .At(cLat, cLng)
        .WithGeofenceRadiusM(200)
        .Build();
    var result = _validator.Validate(new LatLng(pLat, pLng), facility);
    result.IsInside.Should().Be(expected);
}

[Fact]
public void GeofenceValidator_Polygon_PointInside()
{
    // Polygon around Bangkok DC (rough square)
    var wkt = "POLYGON((100.50 13.70, 100.51 13.70, 100.51 13.71, 100.50 13.71, 100.50 13.70))";
    var facility = new FacilityBuilder().WithGeofencePolygon(wkt).Build();
    var inside = new LatLng(13.705, 100.505);
    _validator.Validate(inside, facility).IsInside.Should().BeTrue();
}

[Fact]
public void GeofenceValidator_NoGeofence_AlwaysAccepts()
{
    var facility = new FacilityBuilder().WithoutGeofence().Build();
    _validator.Validate(new LatLng(0, 0), facility).IsInside.Should().BeTrue();
}
```

## Edge Cases & Failure Modes

### Edge Case 1: Polygon Crosses International Date Line

Scenario: Warehouse near Pacific date line (rare for Thailand operations)

**Handling:**
- NTS supports planar geometry; doesn't auto-handle dateline
- Mitigation: store polygons that don't cross dateline (split into 2 polygons if needed)
- Out of scope for Thailand-only operations

### Edge Case 2: GPS Coord at Polygon Boundary

Scenario: Operator standing exactly on polygon edge

**Behavior:**
- NTS `Polygon.Contains()` returns `false` for boundary points (by JTS convention)
- Use `Polygon.Covers()` instead for inclusive boundary
- Or: add ~5m tolerance buffer to polygon

**Decision:** Use `Covers()` (inclusive) — operator on boundary should pass

```csharp
return polygon.Covers(ntsPoint);   // NOT Contains
```

### Edge Case 3: Invalid WKT in Database

Scenario: Data import or manual SQL puts malformed WKT

**Handling:**
- WKTReader throws `ParseException` on invalid input
- Wrap in try/catch in GeofenceValidator
- On parse error: log + treat as "no polygon" + warn ops

```csharp
try { polygon = (Polygon)_wktReader.Read(wkt); }
catch (ParseException ex)
{
    _log.LogError(ex, "Invalid WKT for facility {FacilityId}: {Wkt}", facilityId, wkt);
    return new GeofenceCheckResult(IsInside: true, distanceM: null);   // fail open
}
```

**Trade-off**: fail-open OK for geofence (it's a verification, not security boundary) — fail-closed could lock operator out due to data error

### Edge Case 4: GPS Coordinate Accuracy

Scenario: Operator's GPS has ±50m accuracy (urban canyon, indoor)

**Handling:**
- Operator app sends `gpsCoord` + `accuracyM` (HTTP req optional field)
- If accuracy > 100m: warn operator but accept
- For geofence: expand radius by max(GPS accuracy, 30m) — buffer for normal GPS jitter

```csharp
// in MagicNumbers / OptionsConfig
public int GpsAccuracyBufferM { get; init; } = 30;

// in validator
var effectiveRadius = facility.GeofenceRadiusM + Math.Max(req.GpsAccuracyM ?? 0, 30);
```

### Edge Case 5: Spoofed GPS

Scenario: Operator fakes GPS to appear at warehouse without being there

**Handling:**
- Geofence is **not** anti-fraud measure (audit + photo serve that role)
- Defense: check GPS plausibility (speed delta from last heartbeat must be physically possible)
- Anomaly detection: dispatcher dashboard flags operators with suspicious patterns
- Out of scope for Phase 4; revisit in security hardening

### Edge Case 6: Geofence Disabled in Config

Scenario: `Manual.Features.GeofenceEnforcement = false` (per [ADR-006](adr-006-transport-mode-feature-flag.md))

**Behavior:**
- Validator not called from endpoint (conditional check)
- All pickup/drop accepted regardless of GPS
- Audit log still records `gpsCoord` for forensic later
- Dispatcher console shows badge "Geofence disabled" on each pickup record

## Future Evolution

### Trigger Conditions for PostGIS Migration

อพยพไปใช้ PostGIS เมื่อ:
1. **Spatial queries**: "find all warehouses within 50km" (Fleet provider selection)
2. **Heatmaps**: aggregate trip density by region
3. **Routing**: shortest path between warehouses
4. **Performance**: > 10k geofence checks/second sustained

Migration path:
```sql
-- Add PostGIS extension
CREATE EXTENSION postgis;

-- Add native geometry column
ALTER TABLE facility.warehouses ADD COLUMN geofence_geom geometry(POLYGON, 4326);

-- Populate from WKT
UPDATE facility.warehouses
SET geofence_geom = ST_GeomFromText(geofence_area_wkt, 4326)
WHERE geofence_area_wkt IS NOT NULL;

-- Add spatial index
CREATE INDEX ix_warehouses_geofence ON facility.warehouses USING GIST(geofence_geom);
```

NTS supports PostGIS via Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite — abstraction unchanged

## Consequences

### Positive

- ✓ No PostGIS extension required → keep `postgres:16` image (existing infra)
- ✓ Local dev unchanged
- ✓ Calculation in C# = easier to unit test (no SQL setup)
- ✓ WKT = portable to PostGIS if migrated later
- ✓ NTS library handles polygon contains correctly (no custom geo code)

### Negative

- ✗ No spatial index → can't efficiently answer "warehouses near point" queries
- ✗ WKT parsing per request (cacheable, but adds work)
- ✗ Polygon storage as varchar limits size (~5000 chars = 200-vertex polygon typical)
- ✗ Cross-system queries with spatial criteria require app-side join

### Neutral

- Future migration to PostGIS = additive (WKT preserved as fallback)
- Dispatcher UI Leaflet draw library independent of backend choice

## Acceptance Criteria

- [ ] `IGeofenceValidator` interface in Transport.Manual.Application
- [ ] NetTopologySuite NuGet added to Transport.Manual.Application
- [ ] Facility schema with `geofence_radius_m` + `geofence_area_wkt` columns (mutually exclusive via CHECK constraint)
- [ ] Haversine helper in shared kernel (e.g., Facility.Domain.ValueObjects)
- [ ] GeofenceValidator handles: circle, polygon, no geofence, invalid WKT
- [ ] Capture pickup/drop endpoints integrate validator (conditional on feature flag)
- [ ] Unit tests cover all 3 modes + edge cases (boundary, accuracy buffer, fail-open)
- [ ] Dispatcher console geofence editor (frontend)
- [ ] No PostGIS extension installed (verify in Postgres extensions list)

## Related ADRs

- [ADR-002](adr-002-facility-station-hierarchy.md) — Where Facility geofence fields live (sibling decision)
- [ADR-006](adr-006-transport-mode-feature-flag.md) — `Manual.Features.GeofenceEnforcement` flag
- [ADR-008](adr-008-migration-strategy.md) — No DB extension dependency simplifies migration

## References

- NetTopologySuite: https://github.com/NetTopologySuite/NetTopologySuite
- WKT spec: https://www.ogc.org/standard/sfa/
- Haversine formula: https://en.wikipedia.org/wiki/Haversine_formula
- PostGIS docs (for future): https://postgis.net/documentation/
- react-leaflet-draw: https://github.com/alex3165/react-leaflet-draw
- [Phase 4 — Geofence in pickup capture](../phases/phase-4-transport-manual.md#step-5-mobile-api-endpoints)
- [Manual Operator API — Geofence error response](../api/manual-operator-api.md#post-apioperator-tripstripidpickup)
