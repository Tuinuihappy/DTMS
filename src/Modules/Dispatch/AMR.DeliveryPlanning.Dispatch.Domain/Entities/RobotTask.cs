using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class RobotTask : Entity<Guid>
{
    public Guid TripId { get; private set; }
    public TaskType Type { get; private set; }
    public Enums.TaskStatus Status { get; private set; }
    public int SequenceOrder { get; private set; }
    public Guid? TargetStationId { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    private RobotTask() { }

    internal RobotTask(Guid tripId, TaskType type, int sequenceOrder, Guid? targetStationId)
    {
        Id = Guid.NewGuid();
        TripId = tripId;
        Type = type;
        Status = Enums.TaskStatus.Pending;
        SequenceOrder = sequenceOrder;
        TargetStationId = targetStationId;
    }

    public void MarkDispatched()
    {
        Status = Enums.TaskStatus.Dispatched;
    }

    public void MarkInProgress()
    {
        Status = Enums.TaskStatus.InProgress;
        StartedAt = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = Enums.TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = Enums.TaskStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;
    }

    public void ResetToPending()
    {
        Status = Enums.TaskStatus.Pending;
        StartedAt = null;
    }
}
