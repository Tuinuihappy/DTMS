namespace DTMS.Dispatch.Application.Projections;

/// <summary>
/// Phase P5.2 — Write side of bi.TripFacts. Unlike OrderFacts there is
/// no Trip.Created event, so the projector takes any inbound event as
/// implicit "row exists or create it now with the event's OccurredOn
/// as CreatedAt". Backfill SQL seeds true CreatedAt values from
/// dispatch.Trips for trips that existed before P5.2.
/// </summary>
public interface ITripFactsProjectionStore
{
    Task<bool> HasProcessedEventAsync(string projectorName, Guid eventId, CancellationToken ct);
    Task MarkProcessedAsync(string projectorName, Guid eventId, CancellationToken ct);

    /// <summary>Ensures a row exists for the trip; no-op if already present.</summary>
    Task EnsureRowAsync(
        Guid tripId, DateTime occurredAt,
        Guid? deliveryOrderId, Guid? jobId, CancellationToken ct);

    Task SetStartedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId, Guid? vehicleId,
        string? vendorVehicleKey, CancellationToken ct);

    Task RecordPausedAsync(Guid tripId, DateTime at, CancellationToken ct);
    Task RecordResumedAsync(Guid tripId, DateTime at, CancellationToken ct);

    Task SetCompletedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId, string? vendorUpperKey, CancellationToken ct);

    /// <summary>
    /// Patches VendorVehicleKey for a trip whose row was created without one
    /// (TripStarted carried no vehicle). Fill-only-if-empty; no lifecycle
    /// side effects. Fed by TripVehicleBackfilledIntegrationEventV1.
    /// </summary>
    Task SetVendorVehicleKeyAsync(
        Guid tripId, DateTime at, string vendorVehicleKey, CancellationToken ct);

    Task SetFailedAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct);

    Task SetCancelledAtAsync(
        Guid tripId, DateTime at,
        Guid? deliveryOrderId, Guid? jobId,
        string? vendorUpperKey, string? reason, CancellationToken ct);
}
