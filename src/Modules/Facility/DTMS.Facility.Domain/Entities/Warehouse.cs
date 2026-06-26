using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Facility.Domain.Events;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

/// <summary>
/// First-class warehouse / site / delivery address aggregate. Promoted
/// from the implicit container that <see cref="Map"/> + <see cref="Station"/>
/// used to assume (RIOT3 floor plan = warehouse) into its own concept
/// per ADR-002, so non-AMR modes (Manual, Fleet) can reference physical
/// locations without dragging in factory-local AMR coordinates.
///
/// AMR-specific concepts (Map, Station, Coordinate frames) will move
/// under Transport.Amr in Phase 2.3 and reference Warehouse via FK.
/// For Phase 2.1 the aggregate is purely additive — existing Station
/// code path stays untouched.
///
/// Lifecycle:
///   Active --[Deactivate]--> Inactive --[Activate]--> Active
///
/// Invariants:
///   - Code unique globally (enforced at DB layer)
///   - At least one ServiceMode after creation (can't serve nothing)
///   - Geofence: radius XOR polygon — both can be null (no enforcement)
/// </summary>
public class Warehouse : AggregateRoot<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public LatLng Location { get; private set; } = null!;
    public Address Address { get; private set; } = null!;
    public OperatingHours Hours { get; private set; } = null!;
    public ContactInfo? PrimaryContact { get; private set; }

    // Geofence — radius (simple circle) OR polygon-WKT (complex shape).
    // Both null = no enforcement (e.g. Fleet customer-address destinations
    // where the operator doesn't control the drop point geometry).
    // Storing as WKT (per ADR-010) keeps NetTopologySuite optional in
    // the Domain layer and portable to PostGIS in the future.
    public int? GeofenceRadiusM { get; private set; }
    public string? GeofenceAreaWkt { get; private set; }

    private readonly List<TransportMode> _serviceModes = new();
    public IReadOnlyCollection<TransportMode> ServiceModes => _serviceModes.AsReadOnly();

    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Warehouse() { }

    public static Warehouse Create(
        string code,
        string name,
        LatLng location,
        Address address,
        IReadOnlyList<TransportMode> serviceModes,
        OperatingHours? hours = null,
        ContactInfo? primaryContact = null,
        Guid? id = null,
        DateTime? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
        if (location is null)
            throw new ArgumentNullException(nameof(location));
        if (address is null)
            throw new ArgumentNullException(nameof(address));
        if (serviceModes is null || serviceModes.Count == 0)
            throw new ArgumentException(
                "Warehouse must serve at least one transport mode",
                nameof(serviceModes));

        var warehouse = new Warehouse
        {
            Id = id ?? Guid.NewGuid(),
            Code = code.Trim(),
            Name = name.Trim(),
            Location = location,
            Address = address,
            Hours = hours ?? OperatingHours.AlwaysOpen(),
            PrimaryContact = primaryContact,
            IsActive = true,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        // Dedup defensively — caller might pass [Amr, Amr]. ServiceModes
        // is conceptually a set, but we keep it as a list for predictable
        // EF jsonb serialization order.
        foreach (var mode in serviceModes.Distinct())
            warehouse._serviceModes.Add(mode);

        warehouse.AddDomainEvent(new WarehouseCreatedDomainEvent(
            warehouse.Id, warehouse.Code, warehouse.Name));
        return warehouse;
    }

    public void UpdateLocation(LatLng location, Address address)
    {
        if (location is null) throw new ArgumentNullException(nameof(location));
        if (address is null) throw new ArgumentNullException(nameof(address));
        Location = location;
        Address = address;
        Touch();
    }

    public void UpdateContact(ContactInfo? contact)
    {
        PrimaryContact = contact;
        Touch();
    }

    public void UpdateHours(OperatingHours hours)
    {
        Hours = hours ?? throw new ArgumentNullException(nameof(hours));
        Touch();
    }

    /// <summary>
    /// Configure geofence as a simple radius circle. Pass null to clear.
    /// Mutually exclusive with <see cref="SetGeofencePolygon"/>.
    /// </summary>
    public void SetGeofenceRadius(int? radiusM)
    {
        if (radiusM.HasValue && radiusM.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(radiusM),
                "Radius must be positive");

        GeofenceRadiusM = radiusM;
        GeofenceAreaWkt = null;   // clear polygon — radius wins
        AddDomainEvent(new WarehouseGeofenceUpdatedDomainEvent(Id, radiusM));
        Touch();
    }

    /// <summary>
    /// Configure geofence as a polygon (WKT format, e.g.
    /// "POLYGON((100.5 13.7, ...))"). Pass null to clear.
    /// Mutually exclusive with <see cref="SetGeofenceRadius"/>.
    /// </summary>
    public void SetGeofencePolygon(string? wkt)
    {
        if (wkt is not null && string.IsNullOrWhiteSpace(wkt))
            throw new ArgumentException("WKT cannot be blank string", nameof(wkt));
        if (wkt is not null && !wkt.TrimStart().StartsWith("POLYGON", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "WKT must be a POLYGON geometry (e.g. POLYGON((x1 y1, x2 y2, ...)))",
                nameof(wkt));

        GeofenceAreaWkt = wkt;
        GeofenceRadiusM = null;   // clear radius — polygon wins
        AddDomainEvent(new WarehouseGeofenceUpdatedDomainEvent(Id, null));
        Touch();
    }

    /// <summary>
    /// Enable a transport mode for this warehouse. Idempotent — adding
    /// an already-enabled mode is a no-op (doesn't raise duplicate event).
    /// </summary>
    public void EnableServiceMode(TransportMode mode)
    {
        if (_serviceModes.Contains(mode)) return;
        _serviceModes.Add(mode);
        AddDomainEvent(new WarehouseServiceModeEnabledDomainEvent(Id, mode));
        Touch();
    }

    /// <summary>
    /// Disable a transport mode. Cannot disable the last enabled mode
    /// (warehouse must serve at least one) — caller should
    /// <see cref="Deactivate"/> instead if that's the intent.
    /// </summary>
    public void DisableServiceMode(TransportMode mode)
    {
        if (!_serviceModes.Contains(mode)) return;
        if (_serviceModes.Count == 1)
            throw new InvalidOperationException(
                "Cannot disable the only enabled service mode — Deactivate the warehouse instead");

        _serviceModes.Remove(mode);
        AddDomainEvent(new WarehouseServiceModeDisabledDomainEvent(Id, mode));
        Touch();
    }

    public bool ServesMode(TransportMode mode) => _serviceModes.Contains(mode);

    /// <summary>
    /// Soft-deactivate. Filters out of routing decisions but data
    /// remains intact so existing in-flight trips referencing this
    /// warehouse still resolve.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        AddDomainEvent(new WarehouseDeactivatedDomainEvent(Id));
        Touch();
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
        AddDomainEvent(new WarehouseReactivatedDomainEvent(Id));
        Touch();
    }

    /// <summary>
    /// True if this warehouse can be selected for new orders right now.
    /// (Active AND inside operating hours.)
    /// </summary>
    public bool IsAvailableAt(DateTime localTime) =>
        IsActive && Hours.IsOpenAt(localTime);

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
