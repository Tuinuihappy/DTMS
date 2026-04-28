using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class Trip : AggregateRoot<Guid>
{
    public Guid TenantId { get; private set; }
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

    private readonly List<TripException> _exceptions = new();
    public IReadOnlyCollection<TripException> Exceptions => _exceptions.AsReadOnly();

    private readonly List<ProofOfDelivery> _proofs = new();
    public IReadOnlyCollection<ProofOfDelivery> ProofsOfDelivery => _proofs.AsReadOnly();

    private Trip() { }

    public Trip(Guid tenantId, Guid jobId, Guid vehicleId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
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

    public void Pause()
    {
        if (Status != TripStatus.InProgress)
            throw new InvalidOperationException("Only InProgress trips can be paused.");

        Status = TripStatus.Paused;
        AddDomainEvent(new TripPausedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        RecordEvent(null, "TripPaused", null);
    }

    public void Resume()
    {
        if (Status != TripStatus.Paused)
            throw new InvalidOperationException("Only Paused trips can be resumed.");

        Status = TripStatus.InProgress;
        AddDomainEvent(new TripResumedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id));
        RecordEvent(null, "TripResumed", null);
    }

    public void Cancel(string reason)
    {
        if (Status == TripStatus.Completed || Status == TripStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a trip in {Status} status.");

        Status = TripStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        AddDomainEvent(new TripCancelledDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, reason));
        RecordEvent(null, "TripCancelled", reason);
    }

    public void Reassign(Guid newVehicleId)
    {
        if (Status == TripStatus.Completed || Status == TripStatus.Cancelled)
            throw new InvalidOperationException($"Cannot reassign a trip in {Status} status.");

        var oldVehicleId = VehicleId;
        VehicleId = newVehicleId;

        // Reset dispatched tasks back to Pending so they can be re-sent to new vehicle
        foreach (var task in _tasks.Where(t => t.Status == Enums.TaskStatus.Dispatched || t.Status == Enums.TaskStatus.InProgress))
            task.ResetToPending();

        AddDomainEvent(new TripReassignedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, newVehicleId));
        RecordEvent(null, "TripReassigned", $"From {oldVehicleId} to {newVehicleId}");
    }

    public TripException RaiseException(string code, string severity, string detail)
    {
        var exception = new TripException(Id, code, severity, detail);
        _exceptions.Add(exception);
        AddDomainEvent(new ExceptionRaisedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, exception.Id, code, severity));
        RecordEvent(null, "ExceptionRaised", $"[{severity}] {code}: {detail}");
        return exception;
    }

    public void ResolveException(Guid exceptionId, string resolution, string resolvedBy)
    {
        var exception = _exceptions.FirstOrDefault(e => e.Id == exceptionId)
            ?? throw new InvalidOperationException($"Exception {exceptionId} not found.");

        exception.Resolve(resolution, resolvedBy);
        AddDomainEvent(new ExceptionResolvedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, exceptionId, resolution));
        RecordEvent(null, "ExceptionResolved", $"{exceptionId}: {resolution}");
    }

    public ProofOfDelivery CaptureProofOfDelivery(Guid stopId, string? photoUrl, string? signatureData, List<string>? scannedIds, string? notes)
    {
        var pod = new ProofOfDelivery(Id, stopId, photoUrl, signatureData, scannedIds, notes);
        _proofs.Add(pod);
        AddDomainEvent(new PodCapturedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, stopId));
        RecordEvent(null, "PodCaptured", $"Stop {stopId}");
        return pod;
    }

    private void RecordEvent(Guid? taskId, string eventType, string? details)
    {
        _events.Add(new ExecutionEvent(Id, taskId, eventType, details));
    }
}
