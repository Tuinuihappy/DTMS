using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class Trip : AggregateRoot<Guid>
{
    public Guid JobId { get; private set; }
    public Guid VehicleId { get; private set; }
    public TripStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private readonly List<RobotTask> _tasks = new();
    public IReadOnlyCollection<RobotTask> Tasks => _tasks.AsReadOnly();

    private readonly List<ExecutionEvent> _events = new();
    public IReadOnlyCollection<ExecutionEvent> Events => _events.AsReadOnly();

    private Trip() { }

    public Trip(Guid jobId, Guid vehicleId)
    {
        Id = Guid.NewGuid();
        JobId = jobId;
        VehicleId = vehicleId;
        Status = TripStatus.Created;
        CreatedAt = DateTime.UtcNow;
    }

    public RobotTask AddTask(TaskType type, int sequenceOrder, Guid? targetStationId = null)
    {
        var task = new RobotTask(Id, type, sequenceOrder, targetStationId);
        _tasks.Add(task);
        return task;
    }

    public void Start()
    {
        if (Status != TripStatus.Created)
            throw new InvalidOperationException("Trip can only be started from Created status.");

        Status = TripStatus.InProgress;
        StartedAt = DateTime.UtcNow;

        // Dispatch the first pending task
        var firstTask = _tasks.OrderBy(t => t.SequenceOrder).FirstOrDefault(t => t.Status == Enums.TaskStatus.Pending);
        firstTask?.MarkDispatched();

        AddDomainEvent(new TripStartedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, VehicleId));
        if (firstTask != null)
            AddDomainEvent(new TaskDispatchedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, firstTask.Id, VehicleId));

        RecordEvent(null, "TripStarted", null);
    }

    public void CompleteTask(Guid taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new InvalidOperationException($"Task {taskId} not found.");

        task.MarkCompleted();
        AddDomainEvent(new TaskCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, taskId));
        RecordEvent(taskId, "TaskCompleted", null);

        // Auto-dispatch next pending task
        var nextTask = _tasks.OrderBy(t => t.SequenceOrder).FirstOrDefault(t => t.Status == Enums.TaskStatus.Pending);
        if (nextTask != null)
        {
            nextTask.MarkDispatched();
            AddDomainEvent(new TaskDispatchedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, nextTask.Id, VehicleId));
        }
        else
        {
            // All tasks done → complete the trip
            Status = TripStatus.Completed;
            CompletedAt = DateTime.UtcNow;
            AddDomainEvent(new TripCompletedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
            RecordEvent(null, "TripCompleted", null);
        }
    }

    public void FailTask(Guid taskId, string reason)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new InvalidOperationException($"Task {taskId} not found.");

        task.MarkFailed(reason);
        Status = TripStatus.Failed;
        AddDomainEvent(new TaskFailedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, taskId, reason));
        RecordEvent(taskId, "TaskFailed", reason);
    }

    private void RecordEvent(Guid? taskId, string eventType, string? details)
    {
        _events.Add(new ExecutionEvent(Id, taskId, eventType, details));
    }
}
