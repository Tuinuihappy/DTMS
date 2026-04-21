using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class Zone : Entity<Guid>
{
    public Guid MapId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    
    // Store polygon as a JSON array of coordinates or WKT (Well-Known Text)
    // For simplicity, using a list of coordinates
    private readonly List<Coordinate> _polygon = new();
    public IReadOnlyCollection<Coordinate> Polygon => _polygon.AsReadOnly();
    
    public double? SpeedLimit { get; private set; } // Null if restricted zone

    private Zone() { }

    public Zone(Guid id, Guid mapId, string name, IEnumerable<Coordinate> polygon, double? speedLimit) : base(id)
    {
        MapId = mapId;
        Name = name;
        _polygon.AddRange(polygon);
        SpeedLimit = speedLimit;
    }
}
