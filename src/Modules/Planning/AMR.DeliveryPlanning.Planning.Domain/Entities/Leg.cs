using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

public class Leg : Entity<Guid>
{
    public Guid JobId { get; private set; }
    public Guid FromStationId { get; private set; }
    public Guid ToStationId { get; private set; }
    public int SequenceOrder { get; private set; }
    public double EstimatedCost { get; private set; }

    private readonly List<Stop> _stops = new();
    public IReadOnlyCollection<Stop> Stops => _stops.AsReadOnly();

    private Leg() { } // EF Core

    internal Leg(Guid jobId, Guid fromStationId, Guid toStationId, int sequenceOrder, double estimatedCost)
    {
        Id = Guid.NewGuid();
        JobId = jobId;
        FromStationId = fromStationId;
        ToStationId = toStationId;
        SequenceOrder = sequenceOrder;
        EstimatedCost = estimatedCost;
    }

    public void AddStop(Guid stationId, Enums.StopType type, int sequenceOrder, DateTime? expectedArrival = null)
    {
        _stops.Add(new Stop(Id, stationId, type, sequenceOrder, expectedArrival));
    }
}
