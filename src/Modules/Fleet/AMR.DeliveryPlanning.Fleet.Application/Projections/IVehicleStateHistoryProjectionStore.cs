namespace AMR.DeliveryPlanning.Fleet.Application.Projections;

public interface IVehicleStateHistoryProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken cancellationToken = default);

    Task<(string ToState, DateTime OccurredAt)?> GetLatestForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    Task AppendAsync(
        string projectorName,
        Guid eventId,
        Guid vehicleId,
        string? fromState,
        string toState,
        double batteryLevel,
        Guid? currentNodeId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);
}
