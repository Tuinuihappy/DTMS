using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

public class DeliveryOrderTests
{
    private static AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder CreateOrder(
        string orderRef = "Test Order") =>
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            orderRef, Priority.Normal, requestedDeliveryDate: null);

    private static void AddTestItem(
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder order,
        int itemSeq, string pickup, string drop, string sku,
        double? weightKg = 10.0, double quantity = 5, string uom = "EA") =>
        order.AddItem(
            LocationRef.FromCode(pickup), LocationRef.FromCode(drop),
            itemSeq, sku,
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: weightKg, quantity: quantity, uom: uom,
            cargoType: null, cargoSpecific: null);

    private static IReadOnlyDictionary<LocationRef, Guid> StationMap(params string[] codes)
    {
        var dict = new Dictionary<LocationRef, Guid>();
        foreach (var c in codes)
            dict[LocationRef.FromCode(c)] = Guid.NewGuid();
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
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.Items.Should().HaveCount(1);
        order.Items.First().Sku.Should().Be("SKU-001");
        order.Items.First().PickupLocation.Code.Should().Be("WH-01");
        order.Items.First().DropLocation.Code.Should().Be("STORE-05");
        order.Items.First().ItemSeq.Should().Be(1);
    }

    [Fact]
    public void AddItem_AcceptsStationIdForm()
    {
        var order = CreateOrder();
        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        order.AddItem(
            LocationRef.FromStationId(pickupId), LocationRef.FromStationId(dropId),
            itemSeq: 1, "SKU-X",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0, quantity: 1, uom: "EA",
            cargoType: null, cargoSpecific: null);

        order.Items.Single().PickupLocation.StationId.Should().Be(pickupId);
        order.Items.Single().PickupLocation.IsStationId.Should().BeTrue();
        order.Items.Single().DropLocation.StationId.Should().Be(dropId);
    }

    [Fact]
    public void AddItem_DuplicateSeq_Throws()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        var act = () => AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-002");

        act.Should().Throw<InvalidOperationException>().WithMessage("*seq*1*");
    }

    [Fact]
    public void AddItem_SameSku_DifferentSeq_IsAllowed()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        var act = () => AddTestItem(order, itemSeq: 2, "WH-01", "STORE-05", "SKU-001");

        act.Should().NotThrow();
        order.Items.Should().HaveCount(2);
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
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.Submit();

        order.Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public void MarkAsValidated_SetsStationIdsOnItems()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();

        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        order.MarkAsValidated(new Dictionary<LocationRef, Guid>
        {
            [LocationRef.FromCode("WH-01")] = pickupId,
            [LocationRef.FromCode("STORE-05")] = dropId,
        });

        order.Status.Should().Be(OrderStatus.Validated);
        order.Items.First().PickupStationId.Should().Be(pickupId);
        order.Items.First().DropStationId.Should().Be(dropId);
    }

    [Fact]
    public void MarkAsValidated_WhenNotSubmitted_Throws()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        var act = () => order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Confirm_FromValidated_SetsStatusToConfirmed()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        order.Confirm();

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Should().HaveCount(1);
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
    public void Release_FromHeld_SetsStatusToConfirmed()
    {
        var order = CreateOrder();
        order.Hold("waiting");

        order.Release();

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.DomainEvents.OfType<DeliveryOrderReleasedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Reject_FromSubmitted_SetsStatusToRejected()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();

        order.Reject("invalid station");

        order.Status.Should().Be(OrderStatus.Rejected);
        order.DomainEvents.OfType<DeliveryOrderRejectedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void MarkItemsDelivered_UpdatesMatchingItems()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        AddTestItem(order, itemSeq: 2, "WH-01", "STORE-05", "SKU-002", weightKg: 5.0, quantity: 2);

        order.MarkItemsDelivered(["SKU-001"]);

        order.Items.Single(i => i.Sku == "SKU-001").Status.Should().Be(ItemStatus.Delivered);
        order.Items.Single(i => i.Sku == "SKU-002").Status.Should().Be(ItemStatus.Pending);
    }

    [Fact]
    public void MarkItemsDelivered_IsCaseInsensitive()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.MarkItemsDelivered(["sku-001"]);

        order.Items.Single().Status.Should().Be(ItemStatus.Delivered);
    }

    [Fact]
    public void UpdateDraft_ReplacesCoreFieldsAndItems()
    {
        var order = CreateOrder("Original Ref");
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-OLD", weightKg: 5.0, quantity: 10, uom: "PCS");

        order.UpdateDraft("New Ref", Priority.High, requestedDeliveryDate: null);
        AddTestItem(order, itemSeq: 1, "WH-02", "LINE-02", "SKU-NEW", weightKg: 3.0, quantity: 5, uom: "BOX");

        order.OrderRef.Should().Be("New Ref");
        order.Priority.Should().Be(Priority.High);
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

        order.UpdateDraft(order.OrderRef, order.Priority, requestedDeliveryDate: null);

        order.DomainEvents.OfType<DeliveryOrderDraftUpdatedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void UpdateDraft_WhenNotDraft_Throws()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-001", weightKg: 5.0, quantity: 10, uom: "PCS");
        order.Submit();

        var act = () => order.UpdateDraft("New Ref", Priority.Low, requestedDeliveryDate: null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Draft*");
    }

    [Fact]
    public void UpdateDraft_ClearsTotals()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-A", weightKg: 10.0, quantity: 20, uom: "PCS");
        AddTestItem(order, itemSeq: 2, "WH-01", "LINE-02", "SKU-B", weightKg: 5.0, quantity: 10, uom: "PCS");

        order.UpdateDraft(order.OrderRef, order.Priority, requestedDeliveryDate: null);

        order.Items.Should().BeEmpty();
        order.TotalWeightKg.Should().Be(0);
        order.TotalQuantity.Should().Be(0);
        order.TotalItems.Should().Be(0);
    }

    [Fact]
    public void UpdateDraft_AllowsReAddingItemWithSameSeq()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-REUSE", weightKg: 5.0, quantity: 10, uom: "PCS");

        order.UpdateDraft(order.OrderRef, order.Priority, requestedDeliveryDate: null);
        var act = () => AddTestItem(order, itemSeq: 1, "WH-02", "LINE-02", "SKU-REUSE", weightKg: 3.0, quantity: 5, uom: "BOX");

        act.Should().NotThrow();
    }

    [Fact]
    public void AmendRequestedDeliveryDate_UpdatesFieldAndPreservesStatus()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
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

public class LocationRefTests
{
    [Fact]
    public void FromCode_TrimsInput()
    {
        var r = LocationRef.FromCode("  SHELF-05 ");
        r.Code.Should().Be("SHELF-05");
        r.StationId.Should().BeNull();
        r.IsCode.Should().BeTrue();
    }

    [Fact]
    public void FromCode_RejectsEmpty()
    {
        var act = () => LocationRef.FromCode("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromStationId_RejectsEmptyGuid()
    {
        var act = () => LocationRef.FromStationId(Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_TwoCodeRefs_SameValue_AreEqual()
    {
        LocationRef.FromCode("A").Should().Be(LocationRef.FromCode("A"));
    }

    [Fact]
    public void Equality_CodeVsStationId_AreNotEqual()
    {
        var g = Guid.NewGuid();
        LocationRef.FromCode("A").Should().NotBe(LocationRef.FromStationId(g));
    }

    [Fact]
    public void Equality_TwoDifferentStationIds_AreNotEqual()
    {
        LocationRef.FromStationId(Guid.NewGuid())
            .Should().NotBe(LocationRef.FromStationId(Guid.NewGuid()));
    }
}
