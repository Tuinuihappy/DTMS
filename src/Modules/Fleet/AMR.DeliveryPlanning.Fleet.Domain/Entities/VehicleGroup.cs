using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Entities;

public class VehicleGroup : Entity<Guid>
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public List<string> Tags { get; private set; } = new();

    private readonly List<Guid> _vehicleIds = new();
    public IReadOnlyCollection<Guid> VehicleIds => _vehicleIds.AsReadOnly();

    private VehicleGroup() { }

    public VehicleGroup(Guid tenantId, string name, string description, IEnumerable<string>? tags = null)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Description = description;
        Tags = tags?.ToList() ?? new List<string>();
    }

    public void AddVehicle(Guid vehicleId)
    {
        if (!_vehicleIds.Contains(vehicleId))
            _vehicleIds.Add(vehicleId);
    }

    public void RemoveVehicle(Guid vehicleId) => _vehicleIds.Remove(vehicleId);

    // Called by the repository after loading from the join table.
    // Separated from AddVehicle to keep domain semantics clean.
    internal void LoadVehicleIds(IEnumerable<Guid> vehicleIds)
    {
        _vehicleIds.Clear();
        _vehicleIds.AddRange(vehicleIds);
    }

    public void Update(string name, string description, IEnumerable<string>? tags)
    {
        Name = name;
        Description = description;
        Tags = tags?.ToList() ?? new List<string>();
    }
}
