using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using FluentAssertions;

namespace Dispatch.UnitTests;

public class TripTests
{
    private Trip CreateTripWithTasks()
    {
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid());
        trip.AddTask(TaskType.Move, 1, Guid.NewGuid());
        trip.AddTask(TaskType.Lift, 2);
        trip.AddTask(TaskType.Move, 3, Guid.NewGuid());
        trip.AddTask(TaskType.Drop, 4);
        return trip;
    }

    [Fact]
    public void NewTrip_ShouldHaveCreatedStatus()
    {
        var trip = new Trip(Guid.NewGuid(), Guid.NewGuid());

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
}
