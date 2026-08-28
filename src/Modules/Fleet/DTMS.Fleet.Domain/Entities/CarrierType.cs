using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Entities;

/// <summary>
/// A class of carrier — the rack, cart, or trolley kind that goods ride on
/// (ADR-019). Pairs with <see cref="Carrier"/> the way
/// <see cref="VehicleType"/> pairs with <see cref="Vehicle"/>.
///
/// <para>Moved here from Facility in carrier-tracking P0.2: Facility models
/// static site topology (maps, stations), while a carrier is a movable asset
/// with a lifecycle — Fleet's domain. The table kept its rows through an
/// <c>ALTER TABLE ... SET SCHEMA</c>; see migration 20260827100001.</para>
///
/// <para><b>Not a vendor entity.</b> Unlike <see cref="Vehicle"/> this has no
/// AdapterKey or VendorVehicleKey and never will — RIOT3 has no concept of a
/// carrier and never reports one.</para>
/// </summary>
public class CarrierType : Entity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AmrCapability { get; private set; } = string.Empty;
    public double? MaxWeightKg { get; private set; }
    public int? MaxSlots { get; private set; }
    public string? Description { get; private set; }

    private CarrierType() { }

    public CarrierType(string code, string displayName,
        string amrCapability, double? maxWeightKg = null,
        int? maxSlots = null, string? description = null)
    {
        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        DisplayName = displayName;
        AmrCapability = amrCapability;
        MaxWeightKg = maxWeightKg;
        MaxSlots = maxSlots;
        Description = description;
    }

    public void Update(string displayName, string amrCapability,
        double? maxWeightKg, int? maxSlots, string? description)
    {
        DisplayName = displayName;
        AmrCapability = amrCapability;
        MaxWeightKg = maxWeightKg;
        MaxSlots = maxSlots;
        Description = description;
    }
}
