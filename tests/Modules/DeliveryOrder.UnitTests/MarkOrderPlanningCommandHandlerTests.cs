using DTMS.DeliveryOrder.Application.Commands.MarkOrderPlanning;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// This handler has exactly one caller — DeliveryOrderValidatedConsumer — and
// since 2026-09-25 that caller abandons dispatch when it returns a failure,
// so a cancel landing mid-flight cannot produce trips on a dead order.
//
// That is only safe while a failure means "the domain said no". The moment it
// can also mean "a transient clash, try again", abandoning becomes dropping the
// order on the floor with nothing logged as an error. The distinction lives in
// which exceptions the handler catches, which is what these tests pin.
public class MarkOrderPlanningCommandHandlerTests
{
    [Fact]
    public async Task DomainRefusal_ComesBackAsFailure_NotAsAnException()
    {
        // The consumer reads Result.IsFailure. A throw here would skip its
        // guard entirely and surface as a redelivery loop instead.
        var order = ConfirmedOrder();
        order.Cancel("operator stopped it");
        var (handler, _) = HandlerFor(order);

        var result = await handler.Handle(new MarkOrderPlanningCommand(order.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task ConcurrencyClash_Propagates_SoTheMessageIsRedelivered()
    {
        // The load-bearing one. DeliveryOrderValidatedConsumer treats a failure
        // as "give up on this order", which is right for a domain refusal and
        // badly wrong for a lost write race — the order would silently never
        // dispatch. Keep transient faults on the exception path so MassTransit
        // redelivers.
        //
        // If you came here because this test failed after adding a
        // `catch (DbUpdateConcurrencyException)` to the handler: that change
        // needs the consumer's early return revisited at the same time.
        var order = ConfirmedOrder();
        var (handler, repo) = HandlerFor(order);
        repo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new DbUpdateConcurrencyException("another writer won"));

        var act = () => handler.Handle(new MarkOrderPlanningCommand(order.Id), default);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (MarkOrderPlanningCommandHandler handler, IDeliveryOrderRepository repo)
        HandlerFor(DomainOrder order)
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        repo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return (new MarkOrderPlanningCommandHandler(
            repo, NullLogger<MarkOrderPlanningCommandHandler>.Instance), repo);
    }

    private static DomainOrder ConfirmedOrder()
    {
        var order = DomainOrder.Create(
            "PLAN-" + Guid.NewGuid().ToString("N")[..6],
            Priority.Normal, serviceWindow: null);
        order.AddItem("WH-A", "DOCK-1", 1, "SKU-1", null, null, null, 5.0, Quantity.Create(1, "EA"));
        order.Submit();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        });
        order.Confirm(weightFallbackKg: 5.0);
        return order;
    }
}
