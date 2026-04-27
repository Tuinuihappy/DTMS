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

    private Station() { }

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
