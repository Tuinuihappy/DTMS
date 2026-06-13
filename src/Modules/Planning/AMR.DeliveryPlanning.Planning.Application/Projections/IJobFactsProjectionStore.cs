namespace AMR.DeliveryPlanning.Planning.Application.Projections;

public interface IJobFactsProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct);
    Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct);

    Task UpsertOnCreatedAsync(
        Guid jobId, Guid deliveryOrderId, DateTime createdAt, CancellationToken ct);

    Task SetAssignedAtAsync(Guid jobId, DateTime at, Guid? vehicleId, CancellationToken ct);

    Task SetCommittedAtAsync(Guid jobId, DateTime at, Guid? vehicleId, CancellationToken ct);

    Task SetDispatchedAtAsync(
        Guid jobId, DateTime at, Guid? tripId, string? vendorOrderKey,
        int attemptNumber, CancellationToken ct);

    Task SetExecutingAtAsync(Guid jobId, DateTime at, Guid? tripId, CancellationToken ct);

    Task SetCompletedAtAsync(Guid jobId, DateTime at, Guid? tripId, CancellationToken ct);

    Task SetFailedAtAsync(
        Guid jobId, DateTime at, string? reason, int attemptNumber, CancellationToken ct);

    Task SetCancelledAtAsync(
        Guid jobId, DateTime at, Guid? tripId, string? reason, CancellationToken ct);
}
