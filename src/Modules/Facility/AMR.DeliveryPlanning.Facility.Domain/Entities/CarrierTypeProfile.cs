using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class CarrierTypeProfile : Entity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AMRCapability { get; private set; } = string.Empty;
    public double? MaxWeightKg { get; private set; }
    public int? MaxSlots { get; private set; }
    public string? Description { get; private set; }

    private CarrierTypeProfile() { }

    public CarrierTypeProfile(string code, string displayName,
        string amrCapability, double? maxWeightKg = null,
        int? maxSlots = null, string? description = null)
    {
        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        DisplayName = displayName;
        AMRCapability = amrCapability;
        MaxWeightKg = maxWeightKg;
        MaxSlots = maxSlots;
        Description = description;
    }

    public void Update(string displayName, string amrCapability,
        double? maxWeightKg, int? maxSlots, string? description)
    {
        DisplayName = displayName;
        AMRCapability = amrCapability;
        MaxWeightKg = maxWeightKg;
        MaxSlots = maxSlots;
        Description = description;
    }
}
