using System.Collections.Concurrent;

namespace AMR.DeliveryPlanning.Api.VendorHealth;

public sealed class InMemoryVendorHealthStore : IVendorHealthStore
{
    private readonly ConcurrentDictionary<string, VendorHealthSnapshot> _snapshots = new();

    public event EventHandler<VendorHealthSnapshot>? StatusChanged;

    public VendorHealthSnapshot? Get(string vendor) =>
        _snapshots.TryGetValue(vendor, out var snapshot) ? snapshot : null;

    public void Update(VendorHealthSnapshot snapshot)
    {
        VendorHealthSnapshot? previous = null;
        _snapshots.AddOrUpdate(
            snapshot.Vendor,
            _ => snapshot,
            (_, existing) =>
            {
                previous = existing;
                return snapshot;
            });

        if (previous is null || previous.Status != snapshot.Status)
        {
            StatusChanged?.Invoke(this, snapshot);
        }
    }
}
