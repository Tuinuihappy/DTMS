using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public enum StationType { Normal, Charging, Pickup, Dropoff, Parking, Dock, Checkpoint }

public class Station : Entity<Guid>
{
    public Guid MapId { get; private set; }
    public Guid? ZoneId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Coordinate Coordinate { get; private set; } = default!;
    public StationType Type { get; private set; }
    public List<string> CompatibleVehicleTypes { get; private set; } = new();
    // RIOT3 station identifier — used by RouteEdgeSyncService to call /api/v4/route/costs/.../VendorRef
    public string? VendorRef { get; private set; }
    // Human-readable client-facing identifier (e.g. "WH-NORTH") for use in delivery order submissions
    public string? Code { get; private set; }
    // False when RIOT3 has removed this station — kept for referential integrity, excluded from new orders
    public bool IsActive { get; private set; } = true;

    // Manual override by operator (independent of RIOT3 sync). Survives vendor sync until cleared or expired.
    public bool ManualOverrideOffline { get; private set; }
    public string? ManualOverrideReason { get; private set; }
    public DateTime? ManualOverrideAt { get; private set; }
    public string? ManualOverrideBy { get; private set; }
    public DateTime? ManualOverrideExpiresAt { get; private set; }

    // Vendor action configuration — what the robot should DO at this station
    // (vs. just MOVE there). When ActionType is set, the Dispatch module appends
    // an ACT mission to RIOT3 orders after the MOVE; when null, the station is
    // a pure waypoint. Kept vendor-agnostic at this layer: the dispatcher maps
    // these strings into RIOT3's actionType/category/actionParameters.
    public string? ActionType { get; private set; }
    public string? ActionCategory { get; private set; }
    public Dictionary<string, string>? ActionParameters { get; private set; }

    private Station() { }

    public void SetVendorRef(string vendorRef) => VendorRef = vendorRef;
    public void SetCode(string code) => Code = code.Trim().ToUpperInvariant();
    public void SetType(StationType type) => Type = type;

    /// <summary>
    /// Configure the action a robot performs when reaching this station.
    /// Pass actionType=null to clear the configuration (station becomes a pure
    /// MOVE waypoint). When actionType is set, category defaults to "agv".
    /// Parameters is copied defensively so the caller can keep mutating its dictionary.
    /// </summary>
    public void SetActionConfig(string? actionType, string? category = null, IDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            ActionType = null;
            ActionCategory = null;
            ActionParameters = null;
            return;
        }

        ActionType = actionType.Trim();
        ActionCategory = string.IsNullOrWhiteSpace(category) ? "agv" : category.Trim();
        ActionParameters = parameters is null || parameters.Count == 0
            ? null
            : new Dictionary<string, string>(parameters, StringComparer.Ordinal);
    }
    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
    public void UpdateFromVendor(string name, double x, double y, double yaw)
    {
        Name = name;
        Coordinate = new Coordinate(x, y, yaw);
        // RIOT3 reactivation does NOT override an operator's manual force-offline.
        if (!ManualOverrideOffline || IsManualOverrideExpired(DateTime.UtcNow))
            IsActive = true;
    }

    public void ForceOffline(string reason, string by, TimeSpan duration, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be between 0 and 24 hours.");

        ManualOverrideOffline = true;
        ManualOverrideReason = reason.Trim();
        ManualOverrideBy = string.IsNullOrWhiteSpace(by) ? null : by.Trim();
        ManualOverrideAt = nowUtc;
        ManualOverrideExpiresAt = nowUtc.Add(duration);
    }

    public void ClearManualOverride()
    {
        ManualOverrideOffline = false;
        ManualOverrideReason = null;
        ManualOverrideBy = null;
        ManualOverrideAt = null;
        ManualOverrideExpiresAt = null;
    }

    public bool IsManualOverrideExpired(DateTime nowUtc) =>
        ManualOverrideExpiresAt.HasValue && nowUtc >= ManualOverrideExpiresAt.Value;

    public bool IsCurrentlyManualOffline(DateTime nowUtc) =>
        ManualOverrideOffline && !IsManualOverrideExpired(nowUtc);

    public Station(Guid id, Guid mapId, string name, Coordinate coordinate, StationType type,
        Guid? zoneId = null, IEnumerable<string>? compatibleVehicleTypes = null) : base(id)
    {
        MapId = mapId;
        ZoneId = zoneId;
        Name = name;
        Coordinate = coordinate;
        Type = type;
        CompatibleVehicleTypes = compatibleVehicleTypes?.ToList() ?? new List<string>();
    }
}
