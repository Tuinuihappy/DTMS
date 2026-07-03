namespace DTMS.SharedKernel.Operators;

/// <summary>
/// Cross-module read port for resolving a Manual-mode operator's
/// display name from its Id. The <c>Operator</c> aggregate lives in the
/// Transport.Manual module; modules that only hold an opaque
/// <c>OperatorId</c> (e.g. Dispatch, whose <c>Trip.ClaimedByOperatorId</c>
/// carries no name) use this port to surface a human-readable label
/// without taking a project reference on Transport.Manual.
///
/// Mirrors the existing lookup-service pattern (IStationLookup,
/// IWmsLocationLookup, ISubscriptionLookup). Implemented in
/// Transport.Manual.Infrastructure, wired in ModuleServiceRegistration.
/// </summary>
public interface IOperatorDirectory
{
    /// <summary>
    /// Returns the operator's display name, or <c>null</c> when no
    /// operator with that Id exists (e.g. the Id is stale, or the trip
    /// was never claimed by a Manual operator).
    /// </summary>
    Task<string?> GetDisplayNameAsync(Guid operatorId, CancellationToken ct = default);

    /// <summary>
    /// Batch variant for list views (e.g. the Trips queue): resolves many
    /// operator display names in one round trip to avoid N+1. Ids with no
    /// matching operator are simply absent from the returned map.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> operatorIds, CancellationToken ct = default);
}
