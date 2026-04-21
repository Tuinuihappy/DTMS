using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Entities;

public class VehicleType : Entity<Guid>
{
    public string TypeName { get; private set; } = string.Empty;
    public double MaxPayload { get; private set; }
    
    private readonly List<string> _capabilities = new();
    public IReadOnlyCollection<string> Capabilities => _capabilities.AsReadOnly();

    private VehicleType() { }

    public VehicleType(Guid id, string typeName, double maxPayload, IEnumerable<string> capabilities) : base(id)
    {
        TypeName = typeName;
        MaxPayload = maxPayload;
        _capabilities.AddRange(capabilities);
    }
}
