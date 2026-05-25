using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Time window during which the order must be picked up and delivered.
/// Either bound may be null (open-ended on that side), but at least one must
/// be set. When both are set, Earliest must be on or before Latest.
/// Replaces the single RequestedDeliveryDate scalar so the Planning solver
/// can treat both ends of the window as hard constraints (CVRPTW).
/// </summary>
public class ServiceWindow : ValueObject
{
    public DateTime? Earliest { get; private set; }
    public DateTime? Latest { get; private set; }

    private ServiceWindow() { }

    public static ServiceWindow Create(DateTime? earliest, DateTime? latest)
    {
        if (earliest is null && latest is null)
            throw new ArgumentException("ServiceWindow must have at least one bound (Earliest or Latest).");
        if (earliest.HasValue && latest.HasValue && earliest.Value > latest.Value)
            throw new ArgumentException("ServiceWindow.Earliest must be on or before Latest.");

        return new ServiceWindow { Earliest = earliest, Latest = latest };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Earliest ?? (object)DateTime.MinValue;
        yield return Latest ?? (object)DateTime.MaxValue;
    }
}
