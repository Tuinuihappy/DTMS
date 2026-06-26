namespace DTMS.Fleet.Infrastructure.Projections;

/// <summary>
/// Phase P3.2 — Per-vehicle state-transition log materialized by
/// <c>VehicleStateHistoryProjector</c>. Used by the hourly utilization
/// snapshot service to determine what state each vehicle was in at any
/// historical hour, and by the operator robot drilldown.
/// </summary>
public class VehicleStateHistoryRow
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid VehicleId { get; private set; }
    public string? FromState { get; private set; }
    public string ToState { get; private set; } = string.Empty;
    public double BatteryLevel { get; private set; }
    public Guid? CurrentNodeId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private VehicleStateHistoryRow() { }   // EF

    public VehicleStateHistoryRow(
        Guid eventId, Guid vehicleId,
        string? fromState, string toState,
        double batteryLevel, Guid? currentNodeId,
        DateTime occurredAt)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId is required.", nameof(eventId));
        if (vehicleId == Guid.Empty)
            throw new ArgumentException("VehicleId is required.", nameof(vehicleId));
        if (string.IsNullOrWhiteSpace(toState))
            throw new ArgumentException("ToState is required.", nameof(toState));

        Id = Guid.NewGuid();
        EventId = eventId;
        VehicleId = vehicleId;
        FromState = fromState;
        ToState = toState;
        BatteryLevel = batteryLevel;
        CurrentNodeId = currentNodeId;
        OccurredAt = occurredAt;
    }
}
