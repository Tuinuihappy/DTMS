using DTMS.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Default router implementation. Discovers all
/// <see cref="IVendorEnvelopeOperationService"/> registrations that
/// implement the <see cref="IVendorOperationsAdapter"/> marker, then
/// indexes them by <see cref="IVendorOperationsAdapter.Mode"/>. Robot
/// adapters work the same way.
///
/// Adapters that don't implement the marker (e.g. a no-op fallback
/// registered for unit tests) are ignored — only mode-claiming adapters
/// participate in routing.
/// </summary>
public sealed class VendorOperationsRouter : IVendorOperationsRouter
{
    private readonly IReadOnlyDictionary<TransportMode, IVendorEnvelopeOperationService> _envelopes;
    private readonly IReadOnlyDictionary<TransportMode, IVendorRobotOperationService> _robots;

    public VendorOperationsRouter(
        IEnumerable<IVendorEnvelopeOperationService> envelopeAdapters,
        IEnumerable<IVendorRobotOperationService> robotAdapters)
    {
        _envelopes = BuildMap(envelopeAdapters);
        _robots = BuildMap(robotAdapters);
    }

    public IVendorEnvelopeOperationService For(TransportMode mode) =>
        _envelopes.TryGetValue(mode, out var adapter)
            ? adapter
            : throw new TransportModeNotEnabledException(mode);

    public IVendorRobotOperationService? ForRobot(TransportMode mode) =>
        _robots.TryGetValue(mode, out var adapter) ? adapter : null;

    /// <summary>
    /// Group adapters by their <see cref="IVendorOperationsAdapter.Mode"/>.
    /// Adapters not implementing the marker are skipped. Duplicate Mode
    /// registrations throw — that's a composition root bug.
    /// </summary>
    private static IReadOnlyDictionary<TransportMode, T> BuildMap<T>(IEnumerable<T> adapters)
        where T : class
    {
        var dict = new Dictionary<TransportMode, T>();
        foreach (var adapter in adapters)
        {
            if (adapter is not IVendorOperationsAdapter marker) continue;
            if (dict.ContainsKey(marker.Mode))
                throw new InvalidOperationException(
                    $"Multiple {typeof(T).Name} registrations for mode {marker.Mode}. " +
                    $"Existing: {dict[marker.Mode]!.GetType().FullName}, " +
                    $"Duplicate: {adapter.GetType().FullName}.");
            dict.Add(marker.Mode, adapter);
        }
        return dict;
    }
}
