using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

public class DeliveryOrderTests
{
    private static AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder CreateOrder(
        string orderRef = "Test Order") =>
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            orderRef, Priority.Normal, CargoType.FinishedGood, null);

    private static IReadOnlyDictionary<(string, string), (Guid, Guid)> StationMap(
        params (string pickup, string drop)[] routes)
    {
        var dict = new Dictionary<(string, string), (Guid, Guid)>();
        foreach (var r in routes)
            dict[r] = (Guid.NewGuid(), Guid.NewGuid());
        return dict;
    }

    [Fact]
    public void NewOrder_StartsAsDraft()
    {
        var order = CreateOrder();

        order.Status.Should().Be(OrderStatus.Draft);
        order.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddItem_AddsItemToOrder()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");

        order.Items.Should().HaveCount(1);
        order.Items.First().Sku.Should().Be("SKU-001");
        order.Items.First().PickupLocationCode.Should().Be("WH-01");
    }

    [Fact]
    public void AddItem_DuplicateSku_Throws()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");

        var act = () => order.AddItem("WH-01", "STORE-05", "SKU-001", null, 5.0, 2, "EA");

        act.Should().Throw<InvalidOperationException>().WithMessage("*SKU-001*");
    }

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var order = CreateOrder();

        order.Cancel("No longer needed");

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = CreateOrder();
        order.Cancel("First cancel");

        order.Cancel("Second cancel");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.DomainEvents.OfType<DeliveryOrderCancelledDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Submit_WhenDraft_SetsStatusToSubmitted()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");

        order.Submit();

        order.Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public void MarkAsValidated_SetsStationIdsOnItems()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");
        order.Submit();

        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        order.MarkAsValidated(new Dictionary<(string, string), (Guid, Guid)>
        {
            [("WH-01", "STORE-05")] = (pickupId, dropId)
        });

        order.Status.Should().Be(OrderStatus.Validated);
        order.Items.First().PickupStationId.Should().Be(pickupId);
        order.Items.First().DropStationId.Should().Be(dropId);
    }

    [Fact]
    public void MarkAsValidated_WhenNotSubmitted_Throws()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");

        var act = () => order.MarkAsValidated(StationMap(("WH-01", "STORE-05")));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Hold_SetsStatusToHeld()
    {
        var order = CreateOrder();

        order.Hold("waiting for space");

        order.Status.Should().Be(OrderStatus.Held);
        order.DomainEvents.OfType<DeliveryOrderHeldDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Release_FromHeld_SetsStatusToReadyToPlan()
    {
        var order = CreateOrder();
        order.Hold("waiting");

        order.Release();

        order.Status.Should().Be(OrderStatus.ReadyToPlan);
        order.DomainEvents.OfType<DeliveryOrderReleasedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void MarkItemsDelivered_UpdatesMatchingItems()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");
        order.AddItem("WH-01", "STORE-05", "SKU-002", null, 5.0, 2, "EA");

        order.MarkItemsDelivered(["SKU-001"]);

        order.Items.Single(i => i.Sku == "SKU-001").Status.Should().Be(ItemStatus.Delivered);
        order.Items.Single(i => i.Sku == "SKU-002").Status.Should().Be(ItemStatus.Pending);
    }

    [Fact]
    public void MarkItemsDelivered_IsCaseInsensitive()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "STORE-05", "SKU-001", null, 10.0, 5, "EA");

        order.MarkItemsDelivered(["sku-001"]);

        order.Items.Single().Status.Should().Be(ItemStatus.Delivered);
    }

    [Fact]
    public void UpdateDraft_ReplacesCoreFieldsAndItems()
    {
        var order = CreateOrder("Original Ref");
        order.AddItem("WH-01", "LINE-01", "SKU-OLD", null, 5.0, 10, "PCS");

        order.UpdateDraft("New Ref", Priority.High, CargoType.PackingMaterial, null);
        order.AddItem("WH-02", "LINE-02", "SKU-NEW", null, 3.0, 5, "BOX");

        order.OrderRef.Should().Be("New Ref");
        order.Priority.Should().Be(Priority.High);
        order.CargoType.Should().Be(CargoType.PackingMaterial);
        order.Items.Should().HaveCount(1);
        order.Items.Single().Sku.Should().Be("SKU-NEW");
        order.TotalWeightKg.Should().Be(3.0);
        order.TotalQuantity.Should().Be(5);
        order.TotalItems.Should().Be(1);
    }

    [Fact]
    public void UpdateDraft_RaisesDeliveryOrderDraftUpdatedDomainEvent()
    {
        var order = CreateOrder();

        order.UpdateDraft(order.OrderRef, order.Priority, order.CargoType, null);

        order.DomainEvents.OfType<DeliveryOrderDraftUpdatedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void UpdateDraft_WhenNotDraft_Throws()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "LINE-01", "SKU-001", null, 5.0, 10, "PCS");
        order.Submit();

        var act = () => order.UpdateDraft("New Ref", Priority.Low, CargoType.FinishedGood, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Draft*");
    }

    [Fact]
    public void UpdateDraft_ClearsTotals()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "LINE-01", "SKU-A", null, 10.0, 20, "PCS");
        order.AddItem("WH-01", "LINE-02", "SKU-B", null, 5.0, 10, "PCS");

        order.UpdateDraft(order.OrderRef, order.Priority, order.CargoType, null);

        order.Items.Should().BeEmpty();
        order.TotalWeightKg.Should().Be(0);
        order.TotalQuantity.Should().Be(0);
        order.TotalItems.Should().Be(0);
    }

    [Fact]
    public void UpdateDraft_AllowsReusingSku_ThatWasPreviouslyInOrder()
    {
        var order = CreateOrder();
        order.AddItem("WH-01", "LINE-01", "SKU-REUSE", null, 5.0, 10, "PCS");

        order.UpdateDraft(order.OrderRef, order.Priority, order.CargoType, null);
        var act = () => order.AddItem("WH-02", "LINE-02", "SKU-REUSE", null, 3.0, 5, "BOX");

        act.Should().NotThrow();
    }

    [Fact]
    public void AmendRequestedDeliveryDate_UpdatesFieldAndPreservesStatus()
    {
        var order = CreateOrder();
        order.Submit();
        var newTime = DateTime.UtcNow.AddHours(4);

        order.AmendRequestedDeliveryDate(newTime, "rescheduled");

        order.RequestedDeliveryDate.Should().Be(newTime);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.DomainEvents.OfType<DeliveryOrderAmendedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void AmendRequestedDeliveryDate_WhenDraft_Throws()
    {
        var order = CreateOrder();

        var act = () => order.AmendRequestedDeliveryDate(DateTime.UtcNow.AddHours(1), "reason");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Draft*");
    }
}
