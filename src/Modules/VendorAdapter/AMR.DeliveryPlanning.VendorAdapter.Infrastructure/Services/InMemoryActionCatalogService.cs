using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

public class InMemoryActionCatalogService : IActionCatalogService
{
    private readonly Dictionary<string, ActionCatalogEntry> _catalog = new();

    public InMemoryActionCatalogService()
    {
        SeedDefaults();
    }

    public Task<ActionCatalogEntry?> GetAsync(string vehicleTypeKey, string canonicalAction, CancellationToken _ = default)
    {
        var key = $"{vehicleTypeKey}:{canonicalAction}";
        _catalog.TryGetValue(key, out var entry);
        return Task.FromResult(entry);
    }

    public Task<List<ActionCatalogEntry>> GetAllAsync(CancellationToken _ = default)
        => Task.FromResult(_catalog.Values.ToList());

    public Task UpsertAsync(ActionCatalogEntry entry, CancellationToken _ = default)
    {
        var key = $"{entry.VehicleTypeKey}:{entry.CanonicalAction}";
        _catalog[key] = entry;
        return Task.CompletedTask;
    }

    private void Seed(string vehicleTypeKey, string canonicalAction, string adapterKey, string vendorParams)
    {
        var entry = new ActionCatalogEntry(vehicleTypeKey, canonicalAction, adapterKey, vendorParams);
        _catalog[$"{vehicleTypeKey}:{canonicalAction}"] = entry;
    }

    private void SeedDefaults()
    {
        // ── Lift-up type (RIOT3 actionType=4) ───────────────────────────────
        Seed("liftup", "LIFT", "riot3", "{\"actionType\":\"4\",\"0\":\"1\",\"1\":\"0\"}");
        Seed("liftup", "DROP", "riot3", "{\"actionType\":\"4\",\"0\":\"2\",\"1\":\"0\"}");

        // ── Feeder type (program-number triplet) ─────────────────────────────
        Seed("feeder", "LIFT", "feeder", "{\"program1\":\"192\",\"program2\":\"1\",\"program3\":\"3\"}");
        Seed("feeder", "DROP", "feeder", "{\"program1\":\"192\",\"program2\":\"1\",\"program3\":\"4\"}");
        Seed("feeder", "INIT", "feeder", "{\"program1\":\"192\",\"program2\":\"100\",\"program3\":\"100\"}");
        Seed("feeder", "RIGHT_LOAD",   "feeder", "{\"program1\":\"192\",\"program2\":\"2\",\"program3\":\"3\"}");
        Seed("feeder", "RIGHT_UNLOAD", "feeder", "{\"program1\":\"192\",\"program2\":\"2\",\"program3\":\"4\"}");
    }
}
