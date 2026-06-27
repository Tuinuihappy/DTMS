using DTMS.DeliveryOrder.Domain.Enums;

namespace DTMS.Dispatch.Application.Services;

/// <summary>
/// Default implementation: indexes injected <see cref="IDispatchStrategy"/>
/// instances by <see cref="IDispatchStrategy.Mode"/>. Throws
/// <see cref="TransportModeNotEnabledException"/> for unregistered modes
/// instead of returning null, so callers don't accidentally treat
/// "mode disabled" as "no result".
/// </summary>
public sealed class DispatchStrategyRegistry : IDispatchStrategyRegistry
{
    private readonly IReadOnlyDictionary<TransportMode, IDispatchStrategy> _byMode;

    public DispatchStrategyRegistry(IEnumerable<IDispatchStrategy> strategies)
    {
        // Duplicate Mode = programming error (two strategies claim the
        // same mode). Build the dictionary explicitly so we surface that
        // at startup rather than silently overriding one.
        var dict = new Dictionary<TransportMode, IDispatchStrategy>();
        foreach (var strategy in strategies)
        {
            if (dict.ContainsKey(strategy.Mode))
                throw new InvalidOperationException(
                    $"Multiple IDispatchStrategy registrations for mode {strategy.Mode}. " +
                    $"Existing: {dict[strategy.Mode].GetType().FullName}, " +
                    $"Duplicate: {strategy.GetType().FullName}.");
            dict.Add(strategy.Mode, strategy);
        }
        _byMode = dict;
    }

    public IDispatchStrategy Get(TransportMode mode) =>
        _byMode.TryGetValue(mode, out var strategy)
            ? strategy
            : throw new TransportModeNotEnabledException(mode);

    public bool IsRegistered(TransportMode mode) => _byMode.ContainsKey(mode);
}
