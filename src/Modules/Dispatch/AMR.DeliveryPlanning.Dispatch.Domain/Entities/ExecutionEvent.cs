using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class ExecutionEvent : Entity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid? TaskId { get; private set; }
    public string EventType { get; private set; }
    public string? Details { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private ExecutionEvent() { EventType = null!; }

    internal ExecutionEvent(Guid tripId, Guid? taskId, string eventType, string? details)
    {
        Id = Guid.NewGuid();
        TripId = tripId;
        TaskId = taskId;
        EventType = eventType;
        Details = details;
        OccurredAt = DateTime.UtcNow;
    }
}
