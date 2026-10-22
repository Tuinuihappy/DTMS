using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Events;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using Order = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

/// <summary>"Edit &amp; resend" on a Failed order — ReviseFailed + ResendAfterRevision.</summary>
public class ReviseAndResendTests
{
    private static readonly Dictionary<string, Guid> Stations = new()
    {
        ["ST-A"] = Guid.NewGuid(),
        ["ST-B"] = Guid.NewGuid(),
        ["ST-C"] = Guid.NewGuid(),
    };

    private static Order FailedOrder()
    {
        var order = Order.Create("REV-1", Priority.Normal, serviceWindow: null);
        order.AddItem("ST-A", "ST-B", 1, "SKU-1", null, null, null, 2.0, Quantity.Create(1, "EA"));
        order.AddItem("ST-A", "ST-B", 2, "SKU-2", null, null, null, 3.0, Quantity.Create(2, "EA"));
        order.Submit();
        order.MarkAsValidated(Stations);
        order.Confirm(weightFallbackKg: 500);
        var trip = Guid.NewGuid();
        order.AssignItemsToTrip(trip, 1, Stations["ST-A"], Stations["ST-B"]);
        order.MarkTripItemsFailed(trip, "vendor rejected");
        order.RecomputeStatusFromItems();
        order.Status.Should().Be(OrderStatus.Failed);
        order.ClearDomainEvents();
        return order;
    }

    private static ItemRevision Line(Guid? id, string itemId, string pickup = "ST-A", string drop = "ST-B",
        double weight = 1.0, double qty = 1)
        => new(id, pickup, drop, itemId, null, null, null, weight, Quantity.Create(qty, "EA"));

    private static ServiceWindow Window() => ServiceWindow.Create(DateTime.UtcNow, DateTime.UtcNow.AddHours(2));

    [Fact]
    public void Revise_EditsInPlace_AddsAndRemoves_AndResetsItems()
    {
        var order = FailedOrder();
        var keep = order.Items.Single(i => i.ItemId == "SKU-1");
        var drop = order.Items.Single(i => i.ItemId == "SKU-2");

        var outcome = order.ReviseFailed(Priority.High, Window(),
        [
            Line(keep.Id, "SKU-1", pickup: "ST-C", weight: 4.0, qty: 3),
            Line(null, "SKU-9", weight: 1.5, qty: 2),
        ]);

        outcome.Removed.Should().ContainSingle().Which.Should().BeSameAs(drop);
        outcome.Added.Should().ContainSingle().Which.ItemId.Should().Be("SKU-9");
        order.Items.Should().HaveCount(2);

        keep.PickupLocationCode.Should().Be("ST-C");
        keep.ItemSeq.Should().Be(1);
        keep.Status.Should().Be(ItemStatus.Pending);
        keep.TripId.Should().BeNull();
        keep.PickupStationId.Should().BeNull();
        outcome.Added[0].ItemSeq.Should().Be(3);   // past the removed seq 2

        order.Priority.Should().Be(Priority.High);
        order.TotalItems.Should().Be(2);
        order.TotalWeightKg.Should().Be(5.5);
        order.TotalQuantity.Should().Be(5);
        order.Status.Should().Be(OrderStatus.Failed);   // not resent yet
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resend_BindsNewStations_ConfirmsAndRaisesEvents()
    {
        var order = FailedOrder();
        var keep = order.Items.Single(i => i.ItemId == "SKU-1");
        order.ReviseFailed(Priority.Normal, Window(), [Line(keep.Id, "SKU-1", pickup: "ST-C")]);

        order.ResendAfterRevision(Stations, null, weightFallbackKg: 500, reason: "fixed pickup");

        order.Status.Should().Be(OrderStatus.Confirmed);
        keep.PickupStationId.Should().Be(Stations["ST-C"]);
        keep.DropStationId.Should().Be(Stations["ST-B"]);
        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        confirmed.Items.Should().ContainSingle().Which.PickupStationId.Should().Be(Stations["ST-C"]);
        order.DomainEvents.OfType<DeliveryOrderAmendedDomainEvent>().Should().ContainSingle();
        order.DomainEvents.OfType<DeliveryOrderReopenedDomainEvent>().Should().ContainSingle();
        order.DomainEvents.OfType<DeliveryOrderRedispatchedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Resend_UnknownStationCode_Throws_AndOrderStaysFailed()
    {
        var order = FailedOrder();
        order.ReviseFailed(Priority.Normal, Window(), [Line(null, "SKU-1", pickup: "NOPE")]);

        var act = () => order.ResendAfterRevision(Stations, null, 500, "x");

        act.Should().Throw<InvalidOperationException>().WithMessage("*NOPE*");
        order.Status.Should().Be(OrderStatus.Failed);
    }

    [Fact]
    public void Revise_NonFailedOrder_Throws()
    {
        var order = Order.Create("REV-2", Priority.Normal, serviceWindow: null);
        order.AddItem("ST-A", "ST-B", 1, "SKU-1", null, null, null, 2.0, Quantity.Create(1, "EA"));

        var act = () => order.ReviseFailed(Priority.Normal, Window(), [Line(null, "SKU-1")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Only Failed*");
    }

    [Fact]
    public void Revise_EmptyItems_Throws()
    {
        var act = () => FailedOrder().ReviseFailed(Priority.Normal, Window(), []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least one item*");
    }

    [Fact]
    public void Revise_RepeatedItemId_ThrowsNamingIt_BeforeChangingAnything()
    {
        var order = FailedOrder();
        var before = order.Items.Select(i => i.PickupLocationCode).ToList();

        var act = () => order.ReviseFailed(Priority.High, Window(),
            [Line(order.Items.First().Id, "SKU-7"), Line(null, " SKU-7 ")]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SKU-7*more than once*");
        order.Priority.Should().Be(Priority.Normal);
        order.Items.Select(i => i.PickupLocationCode).Should().Equal(before);
    }

    [Fact]
    public void Revise_ForeignOrDuplicateItemId_Throws()
    {
        var order = FailedOrder();
        var id = order.Items.First().Id;

        var foreign = () => order.ReviseFailed(Priority.Normal, Window(), [Line(Guid.NewGuid(), "X")]);
        var duplicate = () => order.ReviseFailed(Priority.Normal, Window(), [Line(id, "A"), Line(id, "B")]);

        foreign.Should().Throw<InvalidOperationException>().WithMessage("*does not belong*");
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*more than once*");
    }
}
