using DTMS.SharedKernel.Domain;

namespace DTMS.Wms.Domain.Entities;

/// <summary>
/// Snapshot of a physical location fetched from the external WMS
/// (Warehouse Management System). Source of truth is upstream — this
/// entity is populated by the periodic sync service and read by order
/// validation / dispatch grouping.
///
/// LocationCode is what user-facing systems (order Item.PickupLocationCode)
/// reference; ExternalId is the WMS-internal integer key used only for
/// diffing during sync. ParentLocationCode drives operator zone routing
/// (Manual/Fleet transport modes).
///
/// Immutable from DTMS's side — no domain events emitted; DTMS never writes
/// back to WMS. Kept as Entity, not AggregateRoot, for that reason.
/// </summary>
public class WmsLocation : Entity<Guid>
{
    public int ExternalId { get; private set; }
    public string LocationCode { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public int Type { get; private set; }
    public string? TypeName { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsStorageLocation { get; private set; }
    public int? ParentLocationId { get; private set; }
    public string? ParentLocationCode { get; private set; }
    public string? ParentLocationDisplayName { get; private set; }
    public string? Description { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? ZGpsHeight { get; private set; }
    public double? ZTolerance { get; private set; }
    public double? AccuracyMeters { get; private set; }
    public double? HeightDiff { get; private set; }
    public DateTime LastSyncedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid RowVersion { get; private set; }

    private WmsLocation() { }

    public static WmsLocation CreateFromWms(
        int externalId,
        string locationCode,
        string displayName,
        int type,
        string? typeName,
        bool isActive,
        bool isStorageLocation,
        int? parentLocationId,
        string? parentLocationCode,
        string? parentLocationDisplayName,
        string? description,
        double? latitude,
        double? longitude,
        double? zGpsHeight,
        double? zTolerance,
        double? accuracyMeters,
        double? heightDiff,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(locationCode))
            throw new ArgumentException("LocationCode is required.", nameof(locationCode));
        if (externalId <= 0)
            throw new ArgumentOutOfRangeException(nameof(externalId), "ExternalId must be positive.");

        return new WmsLocation
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            LocationCode = locationCode.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? locationCode.Trim() : displayName.Trim(),
            Type = type,
            TypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName.Trim(),
            IsActive = isActive,
            IsStorageLocation = isStorageLocation,
            ParentLocationId = parentLocationId,
            ParentLocationCode = string.IsNullOrWhiteSpace(parentLocationCode) ? null : parentLocationCode.Trim(),
            ParentLocationDisplayName = string.IsNullOrWhiteSpace(parentLocationDisplayName) ? null : parentLocationDisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            ZGpsHeight = zGpsHeight,
            ZTolerance = zTolerance,
            AccuracyMeters = accuracyMeters,
            HeightDiff = heightDiff,
            LastSyncedAt = nowUtc,
            CreatedAt = nowUtc,
            RowVersion = Guid.NewGuid(),
        };
    }

    /// <summary>
    /// Overwrite mutable fields with the latest WMS snapshot. Returns true
    /// if any observable field changed — the sync service uses that to
    /// decide whether to bump RowVersion and emit an upsert log line.
    /// </summary>
    public bool UpdateFromWms(
        string displayName,
        int type,
        string? typeName,
        bool isActive,
        bool isStorageLocation,
        int? parentLocationId,
        string? parentLocationCode,
        string? parentLocationDisplayName,
        string? description,
        double? latitude,
        double? longitude,
        double? zGpsHeight,
        double? zTolerance,
        double? accuracyMeters,
        double? heightDiff,
        DateTime nowUtc)
    {
        var trimmedDisplay = string.IsNullOrWhiteSpace(displayName) ? LocationCode : displayName.Trim();
        var trimmedTypeName = string.IsNullOrWhiteSpace(typeName) ? null : typeName.Trim();
        var trimmedParentCode = string.IsNullOrWhiteSpace(parentLocationCode) ? null : parentLocationCode.Trim();
        var trimmedParentDisplay = string.IsNullOrWhiteSpace(parentLocationDisplayName) ? null : parentLocationDisplayName.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        var changed =
            DisplayName != trimmedDisplay ||
            Type != type ||
            TypeName != trimmedTypeName ||
            IsActive != isActive ||
            IsStorageLocation != isStorageLocation ||
            ParentLocationId != parentLocationId ||
            ParentLocationCode != trimmedParentCode ||
            ParentLocationDisplayName != trimmedParentDisplay ||
            Description != trimmedDescription ||
            Latitude != latitude ||
            Longitude != longitude ||
            ZGpsHeight != zGpsHeight ||
            ZTolerance != zTolerance ||
            AccuracyMeters != accuracyMeters ||
            HeightDiff != heightDiff;

        DisplayName = trimmedDisplay;
        Type = type;
        TypeName = trimmedTypeName;
        IsActive = isActive;
        IsStorageLocation = isStorageLocation;
        ParentLocationId = parentLocationId;
        ParentLocationCode = trimmedParentCode;
        ParentLocationDisplayName = trimmedParentDisplay;
        Description = trimmedDescription;
        Latitude = latitude;
        Longitude = longitude;
        ZGpsHeight = zGpsHeight;
        ZTolerance = zTolerance;
        AccuracyMeters = accuracyMeters;
        HeightDiff = heightDiff;
        LastSyncedAt = nowUtc;
        if (changed) RowVersion = Guid.NewGuid();
        return changed;
    }

    /// <summary>
    /// Soft-delete: the upstream WMS stopped listing this location. In-flight
    /// orders that already reference it still complete; new orders will be
    /// rejected by the validator's IsActive guard.
    /// </summary>
    public void MarkInactive(DateTime nowUtc)
    {
        if (!IsActive) return;
        IsActive = false;
        LastSyncedAt = nowUtc;
        RowVersion = Guid.NewGuid();
    }
}
