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

    private Station() { }

    public void SetVendorRef(string vendorRef) => VendorRef = vendorRef;
    public void SetCode(string code) => Code = code.Trim().ToUpperInvariant();

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
