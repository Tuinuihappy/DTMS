namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Projections;

/// <summary>
/// Phase P3 read-model row materialized by <c>OrderFunnelProjector</c>.
/// One row per hour bucket; columns are status counters incremented
/// every time an order enters that status during the bucket's hour.
///
/// <para>Semantics: this is a <b>transition counter</b> — "how many orders
/// entered status X during hour Y" — not a state snapshot. Sum the
/// confirmed column across hours to get total orders confirmed in the
/// window. To derive a dispatch funnel, the UI reads confirmed/planning/
/// .../completed/failed in time order.</para>
///
/// <para>BucketHour is stored as a UTC timestamp aligned to the start
/// of the hour (date_trunc('hour', occurred_on)) so the primary key
/// uniquely identifies the bucket without composite-key gymnastics.</para>
/// </summary>
public class OrderFunnelHourlyRow
{
    public Guid Id { get; private set; }
    public DateTime BucketHour { get; private set; }

    // One counter column per Order status that has an integration event.
    // Submitted/Validated/Planning/Planned are excluded — no integration
    // event today (Planning/Planned are deliberately internal per the
    // DeliveryOrderStatusIntegrationEvents comment).
    public int Confirmed { get; private set; }
    public int Dispatched { get; private set; }
    public int InProgress { get; private set; }
    public int Completed { get; private set; }
    public int PartiallyCompleted { get; private set; }
    public int Failed { get; private set; }
    public int Cancelled { get; private set; }
    public int Rejected { get; private set; }
    public int Held { get; private set; }
    public int Released { get; private set; }

    private OrderFunnelHourlyRow() { }   // EF

    public OrderFunnelHourlyRow(DateTime bucketHour)
    {
        Id = Guid.NewGuid();
        BucketHour = bucketHour;
    }

    /// <summary>
    /// Increment the counter matching the given status name. Unknown
    /// statuses are silently ignored — projector logs a warning at the
    /// boundary so production drift is observable without a runtime
    /// exception that would block the projection queue.
    /// </summary>
    public void IncrementStatus(string status)
    {
        switch (status)
        {
            case "Confirmed":           Confirmed++; break;
            case "Dispatched":          Dispatched++; break;
            case "InProgress":          InProgress++; break;
            case "Completed":           Completed++; break;
            case "PartiallyCompleted":  PartiallyCompleted++; break;
            case "Failed":              Failed++; break;
            case "Cancelled":           Cancelled++; break;
            case "Rejected":            Rejected++; break;
            case "Held":                Held++; break;
            case "Released":            Released++; break;
            default: /* no-op */        break;
        }
    }
}
