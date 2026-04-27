using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Fleet.Domain.Entities;

public enum MaintenanceType { Scheduled, Corrective, Upgrade }

public class MaintenanceRecord : Entity<Guid>
{
    public Guid VehicleId { get; private set; }
    public MaintenanceType Type { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Technician { get; private set; }
    public DateTime ScheduledAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? Outcome { get; private set; }
    public bool IsCompleted => CompletedAt.HasValue;

    private MaintenanceRecord() { }

    public MaintenanceRecord(Guid vehicleId, MaintenanceType type, string reason, string? technician, DateTime scheduledAt)
    {
        Id = Guid.NewGuid();
        VehicleId = vehicleId;
        Type = type;
        Reason = reason;
        Technician = technician;
        ScheduledAt = scheduledAt;
    }

    public void Complete(string outcome)
    {
        if (IsCompleted) throw new InvalidOperationException("Maintenance already completed.");
        CompletedAt = DateTime.UtcNow;
        Outcome = outcome;
    }
}
