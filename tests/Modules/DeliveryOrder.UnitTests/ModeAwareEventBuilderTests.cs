using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using DeliveryOrderAggregate = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase 3a regression coverage — before this refactor, BuildConfirmedEvent
// hard-derefed `p.PickupStationId!.Value` / `p.DropStationId!.Value`,
// crashing any Confirm() call on a Manual / Fleet order (their items
// have null station Ids by Path A's validation rules). Tests pin:
//   - Manual order Confirm emits ItemEventDto with null station Ids
//     and non-null warehouse Ids (and no exception)
//   - AMR order Confirm emits ItemEventDto with populated station Ids
//     (unchanged from pre-3a behaviour)
//   - DeliveryOrderCreatedDomainEvent (RaiseCreatedEvent path) handles
//     Manual / Fleet items without populating Guid.Empty sentinels
//
// These cover the contract that lets ManualDispatchStrategy (Phase 3c)
// and ManualDispatchStrategy-for-real (Phase 4) consume the event
// without re-checking what mode an order was — the DTO is self-describing.
public class ModeAwareEventBuilderTests
{
    private static DeliveryOrderAggregate ConfirmedAmrOrder()
    {
        var order = DeliveryOrderAggregate.Create(
            "EVT-AMR", Priority.Normal, serviceWindow: null);
        order.AddItem(
            "WH-A", "DOCK-1", 1, "SKU-A",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
        order.Submit();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        });
        order.Confirm(weightFallbackKg: 5);
        return order;
    }

    private static DeliveryOrderAggregate ConfirmedManualOrder(
        out Guid pickupWh, out Guid dropWh)
    {
        var order = DeliveryOrderAggregate.Create(
            "EVT-MANUAL", Priority.Normal, serviceWindow: null,
            sourceSystem: SourceSystem.Manual,
            createdBy: "test", requestedBy: null, notes: null,
            requestedTransportMode: TransportMode.Manual);
        order.AddItem(
            "WH-A", "WH-B", 1, "SKU-M",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
        order.Submit();
        var warehouses = new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["WH-B"] = Guid.NewGuid(),
        };
        order.MarkAsValidated(stationMap: null, warehouseMap: warehouses);
        order.Confirm(weightFallbackKg: 5);
        pickupWh = warehouses["WH-A"];
        dropWh = warehouses["WH-B"];
        return order;
    }

    [Fact]
    public void AmrConfirm_ItemEventDto_CarriesStationIds()
    {
        var order = ConfirmedAmrOrder();

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        var item = confirmed.Items.Single();

        item.PickupStationId.Should().NotBeNull();
        item.DropStationId.Should().NotBeNull();
        item.PickupWarehouseId.Should().BeNull();   // AMR doesn't populate warehouse Ids today
        item.DropWarehouseId.Should().BeNull();
    }

    [Fact]
    public void ManualConfirm_DoesNotThrow_AndEmits_NullStation_NonNullWarehouse()
    {
        // The bug this regression test catches: before Phase 3a,
        // Confirm() on a Manual order threw "Nullable object must have a
        // value" inside BuildConfirmedEvent. Now it must succeed and
        // emit a DTO that downstream Planning / Manual consumers can
        // process without crashing.
        var order = ConfirmedManualOrder(out var pickupWh, out var dropWh);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        var item = confirmed.Items.Single();

        item.PickupStationId.Should().BeNull();
        item.DropStationId.Should().BeNull();
        item.PickupWarehouseId.Should().Be(pickupWh);
        item.DropWarehouseId.Should().Be(dropWh);
    }

    [Fact]
    public void ManualConfirm_PreservesRequestedTransportMode_OnEvent()
    {
        // Downstream Planning consumer routes by RequestedTransportMode
        // (Phase 3c). The event must carry the mode string from the
        // order, not infer "Amr" because items happen to lack stations.
        var order = ConfirmedManualOrder(out _, out _);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();

        confirmed.RequestedTransportMode.Should().Be("Manual");
    }

    [Fact]
    public void ManualCreated_ItemEventDto_NoGuidEmptySentinels()
    {
        // RaiseCreatedEvent used to fall back to Guid.Empty for nullable
        // station Ids ("?? Guid.Empty"). Phase 3a dropped the fallback;
        // the DTO field is now genuinely nullable. Verify Manual items
        // come out as null, not Guid.Empty — downstream projectors
        // depend on null being "ambiguous / not-yet-resolved" while
        // Guid.Empty would look like a real (corrupt) Id.
        var order = DeliveryOrderAggregate.Create(
            "EVT-CR-MANUAL", Priority.Normal, serviceWindow: null,
            sourceSystem: SourceSystem.Manual,
            createdBy: "test", requestedBy: null, notes: null,
            requestedTransportMode: TransportMode.Manual);
        order.AddItem(
            "WH-A", "WH-B", 1, "SKU-M",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
        // Created event fires from RaiseCreatedEvent — call it before
        // Submit so the items still have null station + warehouse Ids
        // (Validated populates them, not the constructor).
        order.RaiseCreatedEvent();

        var created = order.DomainEvents.OfType<DeliveryOrderCreatedDomainEvent>().Single();
        var item = created.Items.Single();

        item.PickupStationId.Should().BeNull();
        item.DropStationId.Should().BeNull();
        item.PickupWarehouseId.Should().BeNull();
        item.DropWarehouseId.Should().BeNull();
        // No Guid.Empty sentinels — was the pre-3a fallback.
        item.PickupStationId.Should().NotBe(Guid.Empty);
        item.DropStationId.Should().NotBe(Guid.Empty);
    }
}
