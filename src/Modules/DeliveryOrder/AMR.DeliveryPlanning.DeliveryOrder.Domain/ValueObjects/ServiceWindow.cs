using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

/// <summary>
/// Time window during which the order must be picked up and delivered.
/// Either bound may be null (open-ended on that side), but at least one must
/// be set. When both are set, EarliestUtc must be on or before LatestUtc.
/// Both bounds are UTC — encoded in the property names so callers can't
/// mistakenly send local time. Replaces the single RequestedDeliveryDate
/// scalar so the Planning solver can treat both ends of the window as hard
/// constraints (CVRPTW).
/// </summary>
public class ServiceWindow : ValueObject
{
    public DateTime? EarliestUtc { get; private set; }
    public DateTime? LatestUtc { get; private set; }

    private ServiceWindow() { }

    public static ServiceWindow Create(DateTime? earliestUtc, DateTime? latestUtc)
    {
        if (earliestUtc is null && latestUtc is null)
            throw new ArgumentException("ServiceWindow must have at least one bound (EarliestUtc or LatestUtc).");
        if (earliestUtc.HasValue && latestUtc.HasValue && earliestUtc.Value > latestUtc.Value)
            throw new ArgumentException("ServiceWindow.EarliestUtc must be on or before LatestUtc.");

        return new ServiceWindow { EarliestUtc = earliestUtc, LatestUtc = latestUtc };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return EarliestUtc ?? (object)DateTime.MinValue;
        yield return LatestUtc ?? (object)DateTime.MaxValue;
    }
}
