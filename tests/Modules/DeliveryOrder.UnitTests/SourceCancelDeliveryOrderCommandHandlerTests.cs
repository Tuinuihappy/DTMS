using DTMS.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.SourceCancelDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// POST /api/v1/source/delivery-orders/{orderRef}/cancel. The cancel itself is
// CancelDeliveryOrderCommand's job; what matters here is who is allowed to name
// an order, and what reaches that command.
public class SourceCancelDeliveryOrderCommandHandlerTests
{
    private const string Owner = "oms";

    [Fact]
    public async Task ForeignRef_IsNotFound_AndNeverReachesTheCancelCommand()
    {
        // The security case. GetByRefAsync filters on both columns, so a ref
        // belonging to another system simply misses — the lookup IS the guard,
        // with no separate check anyone could reorder or forget. This test goes
        // red if someone "optimises" it into a lookup by ref plus an ownership
        // comparison afterwards.
        var (handler, repo, sender) = Build();
        repo.GetByRefAsync("sap", "OD-1", Arg.Any<CancellationToken>()).Returns((DomainOrder?)null);

        var act = () => handler.Handle(
            new SourceCancelDeliveryOrderCommand("sap", "OD-1", "oms-user-42", "x"), default);

        await act.Should().ThrowAsync<NotFoundException>();
        await sender.DidNotReceive().Send(
            Arg.Any<CancelDeliveryOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownRef_AndForeignRef_AreIndistinguishable()
    {
        // An OrderRef runs OD-240926-0147, -0148, … so it can be enumerated.
        // Saying "that one exists but is not yours" would leak another system's
        // order book, which is why this route answers 404 where the trip route
        // answers 403.
        var (handler, repo, _) = Build();
        repo.GetByRefAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((DomainOrder?)null);

        var mine = await Capture(() => handler.Handle(
            new SourceCancelDeliveryOrderCommand(Owner, "OD-MINE", "u", "x"), default));
        var theirs = await Capture(() => handler.Handle(
            new SourceCancelDeliveryOrderCommand(Owner, "OD-THEIRS", "u", "x"), default));

        mine.Should().BeOfType<NotFoundException>();
        theirs.Should().BeOfType<NotFoundException>();
        mine!.Message.Should().NotContain("sap").And.NotContain("belongs to");
    }

    [Fact]
    public async Task ResolvedOrder_ForwardsTheIdAndTrimmedReason()
    {
        var order = ConfirmedOrder();
        var (handler, repo, sender) = Build();
        repo.GetByRefAsync(Owner, order.OrderRef, Arg.Any<CancellationToken>()).Returns(order);

        await handler.Handle(new SourceCancelDeliveryOrderCommand(
            Owner, order.OrderRef, "oms-user-42", "  wrong dock  "), default);

        await sender.Received(1).Send(
            Arg.Is<CancelDeliveryOrderCommand>(c => c.OrderId == order.Id && c.Reason == "wrong dock"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BlankReason_FallsBackToTheLabel_WithoutNamingTheSystem()
    {
        // Reason is non-nullable downstream and becomes the order-activity
        // timeline entry, so it must never arrive empty. It also must not name
        // the caller: the row already carries that as ActorId.
        var order = ConfirmedOrder();
        var (handler, repo, sender) = Build();
        repo.GetByRefAsync(Owner, order.OrderRef, Arg.Any<CancellationToken>()).Returns(order);

        await handler.Handle(new SourceCancelDeliveryOrderCommand(
            Owner, order.OrderRef, "oms-user-42", Reason: "   "), default);

        await sender.Received(1).Send(
            Arg.Is<CancelDeliveryOrderCommand>(c =>
                c.Reason == "Cancelled by source system"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyCancelledOrder_StillDelegates()
    {
        // Guards against importing the trip route's already-cancelled
        // short-circuit. DeliveryOrder.Cancel is a no-op when already cancelled,
        // so the repeat is harmless — and delegating keeps the idempotent item
        // sweep running, which heals orders cancelled before that sweep existed.
        var order = ConfirmedOrder();
        order.Cancel("earlier");
        var (handler, repo, sender) = Build();
        repo.GetByRefAsync(Owner, order.OrderRef, Arg.Any<CancellationToken>()).Returns(order);

        var result = await handler.Handle(new SourceCancelDeliveryOrderCommand(
            Owner, order.OrderRef, "oms-user-42", "again"), default);

        result.IsSuccess.Should().BeTrue();
        await sender.Received(1).Send(
            Arg.Any<CancelDeliveryOrderCommand>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static async Task<Exception?> Capture(Func<Task> act)
    {
        try { await act(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static (SourceCancelDeliveryOrderCommandHandler handler,
                    IDeliveryOrderRepository repo,
                    ISender sender) Build()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var sender = Substitute.For<ISender>();
        // Result has no public parameterless ctor — an unstubbed Send hands back
        // a Task<Result> of null and every assertion downstream NREs.
        sender.Send(Arg.Any<CancelDeliveryOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return (new SourceCancelDeliveryOrderCommandHandler(
            repo, sender, NullLogger<SourceCancelDeliveryOrderCommandHandler>.Instance),
            repo, sender);
    }

    private static DomainOrder ConfirmedOrder()
    {
        var order = DomainOrder.CreateFromUpstream(
            "OD-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal,
            ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
            Owner, "OMS", "OMS", null, null, TransportMode.Manual, false);
        order.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null, 5.0, Quantity.Create(1, "EA"));
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        });
        order.Confirm(weightFallbackKg: 5.0);
        return order;
    }
}
