using DTMS.DeliveryOrder.Application.Commands.MarkOrderDispatched;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// AutoPlan's last step races TripStarted: a self-managed trip starts the
// instant it is dispatched and moves the order to InProgress, so this save
// loses on xmin. Unhandled, that re-ran the whole AutoPlan consumer 60 s
// later only to abandon it. The handler now resolves the race in place:
// reset the tracker, reload, re-apply the forward-only MarkDispatched.
//
// Unlike MarkOrderPlanning (see MarkOrderPlanningCommandHandlerTests), its one
// caller ignores this Result, so resolving here can't silently drop an order;
// a second conflict still propagates for redelivery.
public class MarkOrderDispatchedCommandHandlerTests
{
    [Fact]
    public async Task Conflict_OrderAlreadyInProgress_ResolvesWithoutDispatchingAgain()
    {
        var stale = PlannedOrder();
        var fresh = PlannedOrder();
        fresh.MarkInProgressIfNotYet();          // the TripStarted write that won
        fresh.ClearDomainEvents();
        var (handler, repo) = HandlerFor(stale, fresh, conflicts: 1);

        var result = await handler.Handle(new MarkOrderDispatchedCommand(stale.Id), default);

        result.IsSuccess.Should().BeTrue();
        fresh.Status.Should().Be(OrderStatus.InProgress);
        fresh.DomainEvents.Should().BeEmpty("an order past Dispatched must not emit Dispatched");
        repo.Received(1).ResetTracking();
    }

    [Fact]
    public async Task Conflict_OrderStillPlanned_ReappliesAndSaves()
    {
        // The writer that won didn't move the status (e.g. an item update):
        // without the retry the order would sit at Planned with trips out.
        var stale = PlannedOrder();
        var fresh = PlannedOrder();
        var (handler, repo) = HandlerFor(stale, fresh, conflicts: 1);

        var result = await handler.Handle(new MarkOrderDispatchedCommand(stale.Id), default);

        result.IsSuccess.Should().BeTrue();
        fresh.Status.Should().Be(OrderStatus.Dispatched);
        await repo.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Conflict_OrderAlreadyClosed_IsNotAnError()
    {
        // A self-managed trip can finish within seconds, so the winner may
        // have closed the order already. Nothing left to dispatch.
        var stale = PlannedOrder();
        var fresh = PlannedOrder();
        fresh.Cancel("closed while dispatching");
        var (handler, repo) = HandlerFor(stale, fresh, conflicts: 1);

        var result = await handler.Handle(new MarkOrderDispatchedCommand(stale.Id), default);

        result.IsSuccess.Should().BeTrue();
        fresh.Status.Should().Be(OrderStatus.Cancelled);
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondConflict_Propagates_ForRedelivery()
    {
        var stale = PlannedOrder();
        var fresh = PlannedOrder();
        var (handler, _) = HandlerFor(stale, fresh, conflicts: 2);

        var act = () => handler.Handle(new MarkOrderDispatchedCommand(stale.Id), default);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task NoConflict_MarksDispatched_WithoutReset()
    {
        var order = PlannedOrder();
        var (handler, repo) = HandlerFor(order, order, conflicts: 0);

        var result = await handler.Handle(new MarkOrderDispatchedCommand(order.Id), default);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Dispatched);
        repo.DidNotReceive().ResetTracking();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>The first load returns <paramref name="first"/>, the reload after
    /// ResetTracking returns <paramref name="reloaded"/>; the first
    /// <paramref name="conflicts"/> saves throw a concurrency conflict.</summary>
    private static (MarkOrderDispatchedCommandHandler handler, IDeliveryOrderRepository repo)
        HandlerFor(DomainOrder first, DomainOrder reloaded, int conflicts)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(first, reloaded);

        var saves = 0;
        repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++saves <= conflicts
                ? Task.FromException(new DbUpdateConcurrencyException("another writer won"))
                : Task.CompletedTask);

        return (new MarkOrderDispatchedCommandHandler(
            repo, NullLogger<MarkOrderDispatchedCommandHandler>.Instance), repo);
    }

    // Two instances stand in for "the copy loaded before the conflict" and
    // "the copy reloaded after it"; the repo fake hands them out in order.
    private static DomainOrder PlannedOrder()
    {
        var order = DomainOrder.Create(
            "DISP-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal, serviceWindow: null);
        order.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null, 5.0, Quantity.Create(1, "EA"));
        order.Submit();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        });
        order.Confirm(weightFallbackKg: 5.0);
        order.MarkPlanning();
        order.MarkPlanned();
        order.ClearDomainEvents();
        return order;
    }
}
