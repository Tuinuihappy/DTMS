using DTMS.SharedKernel.Domain;

namespace DTMS.Planning.Domain.Entities;

public class Leg : Entity<Guid>
{
    public Guid JobId { get; private set; }
    public Guid FromStationId { get; private set; }
    public Guid ToStationId { get; private set; }
    public int SequenceOrder { get; private set; }
    public double EstimatedCost { get; private set; }

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
}
