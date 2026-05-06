using AMR.DeliveryPlanning.Facility.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class Shelf : AggregateRoot<Guid>
{
    public Guid MapId { get; private set; }
    public string Rfid { get; private set; } = string.Empty;
    public Guid? CurrentStationId { get; private set; }
    public double MaxWeightKg { get; private set; }
    public int MaxSlots { get; private set; }
    public ShelfStatus Status { get; private set; }

    private Shelf() { }

    public Shelf(Guid mapId, string rfid, double maxWeightKg, int maxSlots)
    {
        Id = Guid.NewGuid();
        MapId = mapId;
        Rfid = rfid;
        MaxWeightKg = maxWeightKg;
        MaxSlots = maxSlots;
        Status = ShelfStatus.Available;
    }

    public void UpdateLocation(Guid? stationId) => CurrentStationId = stationId;
    public void SetInUse() => Status = ShelfStatus.InUse;
    public void SetAvailable() => Status = ShelfStatus.Available;
    public void SetOutOfService() => Status = ShelfStatus.OutOfService;
}
