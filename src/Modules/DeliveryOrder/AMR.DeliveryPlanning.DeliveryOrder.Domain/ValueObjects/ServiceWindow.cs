using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

public sealed class ServiceWindow : ValueObject
{
    public DateTime? Earliest { get; private set; }
    public DateTime? Latest { get; private set; }

    private ServiceWindow() { } // For EF Core

    public ServiceWindow(DateTime? earliest, DateTime? latest)
    {
        Earliest = earliest;
        Latest = latest;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return (object?)Earliest ?? DBNull.Value;
        yield return (object?)Latest ?? DBNull.Value;
    }
}
