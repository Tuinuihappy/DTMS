using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class RouteEdge : Entity<Guid>
{
    public Guid MapId { get; private set; }
    public Guid SourceStationId { get; private set; }
    public Guid TargetStationId { get; private set; }
    public double Distance { get; private set; }
    public double Cost { get; private set; }
    public bool IsBidirectional { get; private set; }

    private RouteEdge() { }

    public RouteEdge(Guid id, Guid mapId, Guid sourceStationId, Guid targetStationId, double distance, double cost, bool isBidirectional = true) : base(id)
    {
        MapId = mapId;
        SourceStationId = sourceStationId;
        TargetStationId = targetStationId;
        Distance = distance;
        Cost = cost;
        IsBidirectional = isBidirectional;
    }
}
