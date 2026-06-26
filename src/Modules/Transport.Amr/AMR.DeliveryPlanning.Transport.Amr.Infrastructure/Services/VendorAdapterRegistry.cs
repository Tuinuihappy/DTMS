using AMR.DeliveryPlanning.Transport.Abstractions.Services;
using AMR.DeliveryPlanning.Transport.Amr.Services;
using AMR.DeliveryPlanning.Transport.Amr.Simulator.Services;

namespace AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Services;

public sealed class VendorAdapterRegistry : IVendorAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IVehicleCommandService> _adapters;

    public VendorAdapterRegistry(
        Riot3CommandService riot3,
        FeederCommandService feeder,
        SimulatorCommandService simulator)
    {
        _adapters = new Dictionary<string, IVehicleCommandService>(StringComparer.OrdinalIgnoreCase)
        {
            ["riot3"] = riot3,
            // Current RIOT3 feeder robots use the same RIOT3 order API. Keep the
            // legacy FeederCommandService available under an explicit key.
            ["feeder"] = riot3,
            ["legacy-feeder"] = feeder,
            ["sim"] = simulator,
            ["simulator"] = simulator
        };
    }

    public IReadOnlyCollection<string> RegisteredKeys => _adapters.Keys.ToArray();

    public IVehicleCommandService? Resolve(string adapterKey)
    {
        if (string.IsNullOrWhiteSpace(adapterKey))
            return null;

        return _adapters.TryGetValue(adapterKey.Trim(), out var adapter)
            ? adapter
            : null;
    }
}
