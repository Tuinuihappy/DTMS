using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

// Mirrors the RIOT3 /api/v4/order/order-templates payload shape:
//   { name, priority, transportOrder { structureType, priority, missions[] },
//     appointVehicle*, appointQueueWaitArea }
//
// The missions array is stored verbatim as a JSON document under "Missions"
// (jsonb). Each mission is either:
//   - MOVE (mapId + stationId)
//   - ACT with inline params (actionType + blockingType + actionParameters[])
//   - ACT by reference (actionTemplateName) — resolved against
//     Planning.Domain.Entities.ActionTemplate at dispatch time
//
// Storing missions inline (rather than in a separate table) keeps reads cheap
// and matches the way the rest of the system thinks of OrderTemplate as a
// snapshot the dispatcher pulls + flattens just before POSTing to RIOT3.
public class OrderTemplate : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public string StructureType { get; private set; } = "sequence";
    public int TransportOrderPriority { get; private set; }

    // Vendor binding hints — empty/whitespace is collapsed to null on the way in
    // because RIOT3's UI sends "" for unset values and we don't want to treat
    // that differently from "not provided".
    public string? AppointVehicleKey { get; private set; }
    public string? AppointVehicleName { get; private set; }
    public string? AppointVehicleGroupKey { get; private set; }
    public string? AppointVehicleGroupName { get; private set; }
    public string? AppointQueueWaitArea { get; private set; }

    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ModifiedAt { get; private set; }

    public IReadOnlyList<OrderTemplateMission> Missions { get; private set; } = new List<OrderTemplateMission>();

    private OrderTemplate() { } // EF Core

    public OrderTemplate(
        string name,
        int priority,
        string structureType,
        int transportOrderPriority,
        IEnumerable<OrderTemplateMission> missions,
        string? appointVehicleKey = null,
        string? appointVehicleName = null,
        string? appointVehicleGroupKey = null,
        string? appointVehicleGroupName = null,
        string? appointQueueWaitArea = null,
        string? description = null)
    {
        Id = Guid.NewGuid();
        SetName(name);
        Priority = priority;
        SetStructureType(structureType);
        TransportOrderPriority = transportOrderPriority;
        SetMissions(missions);
        AppointVehicleKey = Normalize(appointVehicleKey);
        AppointVehicleName = Normalize(appointVehicleName);
        AppointVehicleGroupKey = Normalize(appointVehicleGroupKey);
        AppointVehicleGroupName = Normalize(appointVehicleGroupName);
        AppointQueueWaitArea = Normalize(appointQueueWaitArea);
        Description = Normalize(description);
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(
        int priority,
        string structureType,
        int transportOrderPriority,
        IEnumerable<OrderTemplateMission> missions,
        string? appointVehicleKey,
        string? appointVehicleName,
        string? appointVehicleGroupKey,
        string? appointVehicleGroupName,
        string? appointQueueWaitArea,
        string? description)
    {
        Priority = priority;
        SetStructureType(structureType);
        TransportOrderPriority = transportOrderPriority;
        SetMissions(missions);
        AppointVehicleKey = Normalize(appointVehicleKey);
        AppointVehicleName = Normalize(appointVehicleName);
        AppointVehicleGroupKey = Normalize(appointVehicleGroupKey);
        AppointVehicleGroupName = Normalize(appointVehicleGroupName);
        AppointQueueWaitArea = Normalize(appointQueueWaitArea);
        Description = Normalize(description);
        ModifiedAt = DateTime.UtcNow;
    }

    public void Rename(string newName)
    {
        SetName(newName);
        ModifiedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        ModifiedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        ModifiedAt = DateTime.UtcNow;
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("OrderTemplate Name must not be empty.", nameof(name));
        if (name.Length > 100)
            throw new ArgumentException("OrderTemplate Name must be 100 characters or fewer.", nameof(name));
        Name = name.Trim();
    }

    private void SetStructureType(string structureType)
    {
        if (string.IsNullOrWhiteSpace(structureType))
            throw new ArgumentException("StructureType must not be empty.", nameof(structureType));
        var s = structureType.Trim();
        // RIOT3 v4 supports "sequence" (linear) and "tree" (behavior tree).
        if (s != "sequence" && s != "tree")
            throw new ArgumentException("StructureType must be 'sequence' or 'tree'.", nameof(structureType));
        StructureType = s;
    }

    private void SetMissions(IEnumerable<OrderTemplateMission> missions)
    {
        if (missions is null) throw new ArgumentNullException(nameof(missions));
        var list = missions.ToList();
        if (list.Count == 0)
            throw new ArgumentException("OrderTemplate must have at least one mission.", nameof(missions));

        // Re-number sequences from 1 so the stored order matches list order even
        // if the caller used non-contiguous or unordered values.
        var ordered = list.OrderBy(m => m.Sequence).ToList();
        var renumbered = new List<OrderTemplateMission>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
            renumbered.Add(ordered[i].WithSequence(i + 1));
        Missions = renumbered;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
