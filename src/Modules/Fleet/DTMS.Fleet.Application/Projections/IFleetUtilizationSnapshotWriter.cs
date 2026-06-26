namespace AMR.DeliveryPlanning.Fleet.Application.Projections;

/// <summary>
/// Phase P3.2 — Background-service hook for snapshotting fleet
/// utilization. Implementation walks the live <c>Vehicles</c> table,
/// counts each state, and UPSERTs a row into FleetUtilizationHourly for
/// the current hour bucket. Idempotent at the hour grain.
/// </summary>
public interface IFleetUtilizationSnapshotWriter
{
    Task UpsertCurrentBucketAsync(CancellationToken cancellationToken = default);
}
