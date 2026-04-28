namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Data;

// Persistence-only entity — not a domain concept.
// Represents one row in fleet.VehicleGroupMembers join table.
internal sealed class VehicleGroupMember
{
    public Guid VehicleGroupId { get; init; }
    public Guid VehicleId { get; init; }
}
