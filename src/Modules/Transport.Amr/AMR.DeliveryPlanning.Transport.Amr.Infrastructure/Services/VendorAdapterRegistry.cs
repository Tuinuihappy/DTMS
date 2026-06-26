using AMR.DeliveryPlanning.Transport.Abstractions.Services;
using AMR.DeliveryPlanning.Transport.Amr.Services;

namespace AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Services;

public sealed class VendorAdapterRegistry : IVendorAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IVehicleCommandService> _adapters;

    public VendorAdapterRegistry(Riot3CommandService riot3)
    {
        _adapters = new Dictionary<string, IVehicleCommandService>(StringComparer.OrdinalIgnoreCase)
        {
            ["riot3"] = riot3,
            // Feeder-type robots use the same Riot3 order API as liftup.
            ["feeder"] = riot3,
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
