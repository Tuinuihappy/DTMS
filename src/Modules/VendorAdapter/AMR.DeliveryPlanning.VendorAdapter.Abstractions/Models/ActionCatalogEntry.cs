using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;

public class ActionCatalogEntry : Entity<Guid>
{
    public string VehicleTypeKey { get; private set; } = string.Empty;
    public string CanonicalAction { get; private set; } = string.Empty;
    public string AdapterKey { get; private set; } = string.Empty;
    public string VendorParamsJson { get; private set; } = "{}";

    private ActionCatalogEntry() { }

    public ActionCatalogEntry(string vehicleTypeKey, string canonicalAction, string adapterKey, string vendorParamsJson)
    {
        Id = Guid.NewGuid();
        VehicleTypeKey = vehicleTypeKey;
        CanonicalAction = canonicalAction;
        AdapterKey = adapterKey;
        VendorParamsJson = vendorParamsJson;
    }

    public Dictionary<string, string> ParseVendorParams()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(VendorParamsJson) ?? new(); }
        catch { return new(); }
    }
}
