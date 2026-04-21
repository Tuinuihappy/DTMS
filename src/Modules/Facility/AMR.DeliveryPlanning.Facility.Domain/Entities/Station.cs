using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public enum StationType
{
    Normal,
    Charging,
    Pickup,
    Dropoff
}

public class Station : Entity<Guid>
{
    public Guid MapId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Coordinate Coordinate { get; private set; } = default!;
    public StationType Type { get; private set; }

    private Station() { }

    public Station(Guid id, Guid mapId, string name, Coordinate coordinate, StationType type) : base(id)
    {
        MapId = mapId;
        Name = name;
        Coordinate = coordinate;
        Type = type;
    }
}
