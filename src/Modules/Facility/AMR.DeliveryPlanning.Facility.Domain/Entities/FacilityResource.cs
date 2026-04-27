using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public enum FacilityResourceType { Door, AirShowerDoor, Elevator, Charger, Stopper, TrafficArea }

public class FacilityResource : Entity<Guid>
{
    public Guid MapId { get; private set; }
    public string ResourceKey { get; private set; } = string.Empty;
    public FacilityResourceType ResourceType { get; private set; }
    public string? VendorRef { get; private set; }
    public string? Description { get; private set; }

    private FacilityResource() { }

    public FacilityResource(Guid mapId, string resourceKey, FacilityResourceType resourceType,
        string? vendorRef = null, string? description = null)
    {
        Id = Guid.NewGuid();
        MapId = mapId;
        ResourceKey = resourceKey;
        ResourceType = resourceType;
        VendorRef = vendorRef;
        Description = description;
    }
}
