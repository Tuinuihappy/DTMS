using AMR.DeliveryPlanning.Facility.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class Map : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public double Width { get; private set; }
    public double Height { get; private set; }
    public string MapData { get; private set; } = string.Empty; // JSON grid representation
    // RIOT3 map identifier — used by RouteEdgeSyncService to call /api/v4/route/costs/{VendorRef}/...
    public string? VendorRef { get; private set; }
    
    private readonly List<Station> _stations = new();
    public IReadOnlyCollection<Station> Stations => _stations.AsReadOnly();

    private readonly List<Zone> _zones = new();
    public IReadOnlyCollection<Zone> Zones => _zones.AsReadOnly();

    private readonly List<RouteEdge> _routeEdges = new();
    public IReadOnlyCollection<RouteEdge> RouteEdges => _routeEdges.AsReadOnly();

    private Map() { }

    public Map(Guid id, string name, string version, double width, double height, string mapData) : base(id)
    {
        Name = name;
        Version = version;
        Width = width;
        Height = height;
        MapData = mapData;

        AddDomainEvent(new MapCreatedDomainEvent(this.Id, this.Name));
    }

    public void AddStation(Station station)
    {
        // Add business rules e.g., coordinate must be within map bounds
        if (station.Coordinate.X < 0 || station.Coordinate.X > Width ||
            station.Coordinate.Y < 0 || station.Coordinate.Y > Height)
        {
            throw new ArgumentException("Station coordinate is out of map bounds.");
        }

        _stations.Add(station);
        AddDomainEvent(new StationAddedDomainEvent(this.Id, station.Id));
    }

    public void AddZone(Zone zone)
    {
        _zones.Add(zone);
    }

    public void SetVendorRef(string vendorRef) => VendorRef = vendorRef;

    public void AddRouteEdge(RouteEdge edge)
    {
        // Verify source and target stations exist in this map
        if (!_stations.Any(s => s.Id == edge.SourceStationId) || 
            !_stations.Any(s => s.Id == edge.TargetStationId))
        {
            throw new ArgumentException("Source or Target station does not exist in this map.");
        }

        _routeEdges.Add(edge);
    }
}
