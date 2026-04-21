using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

public class Stop : Entity<Guid>
{
    public Guid LegId { get; private set; }
    public Guid StationId { get; private set; }
    public StopType Type { get; private set; }
    public int SequenceOrder { get; private set; }
    public DateTime? ExpectedArrival { get; private set; }

    private Stop() { } // EF Core

    internal Stop(Guid legId, Guid stationId, StopType type, int sequenceOrder, DateTime? expectedArrival = null)
    {
        Id = Guid.NewGuid();
        LegId = legId;
        StationId = stationId;
        Type = type;
        SequenceOrder = sequenceOrder;
        ExpectedArrival = expectedArrival;
    }
}
