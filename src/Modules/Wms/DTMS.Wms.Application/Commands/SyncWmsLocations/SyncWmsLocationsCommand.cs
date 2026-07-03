using DTMS.SharedKernel.Messaging;

namespace DTMS.Wms.Application.Commands.SyncWmsLocations;

/// <summary>
/// Pull the full WMS location catalogue and reconcile against
/// <c>wms.Locations</c>. Idempotent — safe to run concurrently with itself
/// (handler holds a global semaphore) and safe to retry on transient
/// upstream failure.
/// </summary>
public record SyncWmsLocationsCommand : ICommand<SyncWmsLocationsResult>;

public record SyncWmsLocationsResult(
    int Pulled,
    int Added,
    int Updated,
    int Deactivated,
    int Reactivated,
    long ElapsedMs);
