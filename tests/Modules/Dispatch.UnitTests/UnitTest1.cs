using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using FluentAssertions;

namespace Dispatch.UnitTests;

public class TripTests
{
    private Trip CreateTripWithTasks()
    {
        var trip = new Trip(Guid.Empty, Guid.NewGuid(), Guid.NewGuid());
        trip.AddTask(TaskType.Move, 1, Guid.NewGuid());
        trip.AddTask(TaskType.Lift, 2);
        trip.AddTask(TaskType.Move, 3, Guid.NewGuid());
        trip.AddTask(TaskType.Drop, 4);
        return trip;
    }

    [Fact]
    public void NewTrip_ShouldHaveCreatedStatus()
    {
        var trip = new Trip(Guid.Empty, Guid.NewGuid(), Guid.NewGuid());

        trip.Status.Should().Be(TripStatus.Created);
        trip.Tasks.Should().BeEmpty();
        trip.StartedAt.Should().BeNull();
    }

    [Fact]
    public void AddTask_ShouldAddToCollection()
    {
        var trip = CreateTripWithTasks();

        trip.Tasks.Should().HaveCount(4);
        trip.Tasks.Select(t => t.Type).Should().ContainInOrder(
            TaskType.Move, TaskType.Lift, TaskType.Move, TaskType.Drop);
    }

    [Fact]
    public void Start_ShouldDispatchFirstTask()
    {
        var trip = CreateTripWithTasks();

        trip.Start();

        trip.Status.Should().Be(TripStatus.InProgress);
        trip.StartedAt.Should().NotBeNull();
        var firstTask = trip.Tasks.OrderBy(t => t.SequenceOrder).First();
        firstTask.Status.Should().Be(AMR.DeliveryPlanning.Dispatch.Domain.Enums.TaskStatus.Dispatched);
    }

    [Fact]
    public void Start_FromNonCreated_ShouldThrow()
    {
        var trip = CreateTripWithTasks();
        trip.Start();

        var act = () => trip.Start();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CompleteTask_ShouldAutoDispatchNext()
    {
        var trip = CreateTripWithTasks();
        trip.Start();

        var firstTask = trip.Tasks.OrderBy(t => t.SequenceOrder).First();
        trip.CompleteTask(firstTask.Id);

        firstTask.Status.Should().Be(AMR.DeliveryPlanning.Dispatch.Domain.Enums.TaskStatus.Completed);
        var secondTask = trip.Tasks.OrderBy(t => t.SequenceOrder).ElementAt(1);
        secondTask.Status.Should().Be(AMR.DeliveryPlanning.Dispatch.Domain.Enums.TaskStatus.Dispatched);
        trip.Status.Should().Be(TripStatus.InProgress); // not yet done
    }

    [Fact]
    public void CompleteAllTasks_ShouldCompleteTrip()
    {
        var trip = CreateTripWithTasks();
        trip.Start();

        foreach (var task in trip.Tasks.OrderBy(t => t.SequenceOrder))
        {
            trip.CompleteTask(task.Id);
        }

        trip.Status.Should().Be(TripStatus.Completed);
        trip.CompletedAt.Should().NotBeNull();
        trip.Tasks.Should().OnlyContain(t => t.Status == AMR.DeliveryPlanning.Dispatch.Domain.Enums.TaskStatus.Completed);
    }

    [Fact]
    public void FailTask_ShouldFailEntireTrip()
    {
        var trip = CreateTripWithTasks();
        trip.Start();

        var firstTask = trip.Tasks.OrderBy(t => t.SequenceOrder).First();
        trip.FailTask(firstTask.Id, "Motor malfunction");

        trip.Status.Should().Be(TripStatus.Failed);
        firstTask.Status.Should().Be(AMR.DeliveryPlanning.Dispatch.Domain.Enums.TaskStatus.Failed);
        firstTask.FailureReason.Should().Be("Motor malfunction");
    }

    [Fact]
    public void CompleteTask_WithInvalidId_ShouldThrow()
    {
        var trip = CreateTripWithTasks();
        trip.Start();

        var act = () => trip.CompleteTask(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Events_ShouldBeRecorded()
    {
        var trip = CreateTripWithTasks();
        trip.Start();

        trip.Events.Should().HaveCount(1); // TripStarted
        trip.Events.First().EventType.Should().Be("TripStarted");
    }

    // ── SetAssignedVehicle ─────────────────────────────────────────────────
    // RIOT3 may auto-select the robot when we submit an order without
    // appointVehicleKey. The chosen robot key arrives later via the
    // task callback (processingVehicle.key), at which point we bind it
    // to the trip without disturbing tasks the vendor is already running.

    [Fact]
    public void SetAssignedVehicle_FromNull_ShouldBindVehicleAndEmitEvent()
    {
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid(), null);
        trip.AddTask(TaskType.Move, 1, Guid.NewGuid());
        trip.Start();
        var firstTask = trip.Tasks.First();
        var vehicleId = Guid.NewGuid();

        trip.SetAssignedVehicle(vehicleId);

        trip.VehicleId.Should().Be(vehicleId);
        firstTask.Status.Should().Be(AMR.DeliveryPlanning.Dispatch.Domain.Enums.TaskStatus.Dispatched,
            "binding a vehicle must NOT reset task state — the vendor is already executing");
        trip.DomainEvents.Should().ContainSingle(e => e is TripVehicleAssignedDomainEvent);
    }

    [Fact]
    public void SetAssignedVehicle_WithSameVehicle_ShouldBeIdempotent()
    {
        var vehicleId = Guid.NewGuid();
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid(), null);
        trip.SetAssignedVehicle(vehicleId);
        // Clear the first event so we can assert no extra event on the second call.
        trip.ClearDomainEvents();

        trip.SetAssignedVehicle(vehicleId);

        trip.VehicleId.Should().Be(vehicleId);
        trip.DomainEvents.Should().NotContain(e => e is TripVehicleAssignedDomainEvent);
    }

    [Fact]
    public void SetAssignedVehicle_WhenDifferentVehicleAlreadyAssigned_ShouldThrow()
    {
        // A different vendor key arriving on a trip that already has a
        // vehicle is a reassignment, which is a different operation (it
        // resets tasks to Pending so they can be re-dispatched).
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid(), null);
        trip.SetAssignedVehicle(Guid.NewGuid());

        var act = () => trip.SetAssignedVehicle(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Reassign*");
    }

    [Fact]
    public void SetAssignedVehicle_WithEmptyGuid_ShouldThrow()
    {
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid(), null);

        var act = () => trip.SetAssignedVehicle(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetAssignedVehicle_OnTerminalTrip_ShouldThrow()
    {
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid(), null);
        trip.AddTask(TaskType.Move, 1, Guid.NewGuid());
        trip.Start();
        trip.Cancel("test");

        var act = () => trip.SetAssignedVehicle(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }
}
