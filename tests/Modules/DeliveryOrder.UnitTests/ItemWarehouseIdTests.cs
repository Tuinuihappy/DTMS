using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

// Phase 2.5 — Item carries WarehouseId alongside StationId. The slots are
// nullable until Phase 2.6 wires IWarehouseLookup into the validation
// pipeline; these tests pin the SetWarehouseIds setter contract so Phase
// 2.6's resolver has a clear target.
public class ItemWarehouseIdTests
{
    [Fact]
    public void NewItem_HasNullWarehouseIds()
    {
        // Defaults must be null — order intake creates the Item before
        // warehouse resolution runs. If they were Guid.Empty (the wrong
        // "no value" sentinel) downstream code would not be able to tell
        // "not resolved yet" from "resolved to nothing".
        var item = NewItem();

        item.PickupWarehouseId.Should().BeNull();
        item.DropWarehouseId.Should().BeNull();
    }

    [Fact]
    public void SetWarehouseIds_ValidGuids_StoresBoth()
    {
        var item = NewItem();
        var pickup = Guid.NewGuid();
        var drop = Guid.NewGuid();

        item.SetWarehouseIds(pickup, drop);

        item.PickupWarehouseId.Should().Be(pickup);
        item.DropWarehouseId.Should().Be(drop);
    }

    [Fact]
    public void SetWarehouseIds_EmptyPickup_Throws()
    {
        // Guid.Empty is the historically-confused "unset" sentinel. Reject
        // it explicitly so callers can't accidentally store it as a "valid"
        // warehouse Id — the column should only ever hold real warehouse Ids
        // or null.
        var item = NewItem();

        var act = () => item.SetWarehouseIds(Guid.Empty, Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithParameterName("pickupWarehouseId");
    }

    [Fact]
    public void SetWarehouseIds_EmptyDrop_Throws()
    {
        var item = NewItem();

        var act = () => item.SetWarehouseIds(Guid.NewGuid(), Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("dropWarehouseId");
    }

    [Fact]
    public void SetWarehouseIds_Twice_OverwritesPrevious()
    {
        // Re-resolution is allowed — the lookup might be re-run after an
        // edit. Final values take precedence; no immutability invariant
        // here (the eventual "required after Phase 2.6" constraint will
        // live at validation layer, not the setter).
        var item = NewItem();
        var p1 = Guid.NewGuid();
        var d1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        item.SetWarehouseIds(p1, d1);
        item.SetWarehouseIds(p2, d2);

        item.PickupWarehouseId.Should().Be(p2);
        item.DropWarehouseId.Should().Be(d2);
    }

    // Helper — minimal valid Item construction. Mirrors what the order
    // intake pipeline produces before any resolution runs.
    private static Item NewItem()
    {
        // Internal constructor — accessible because tests are in same
        // assembly via InternalsVisibleTo on DeliveryOrder.Domain.
        // (If that's missing the test fails to compile, which is fine —
        // we'll add the attribute then.)
        return new Item(
            deliveryOrderId: Guid.NewGuid(),
            pickupLocationCode: "WH-BKK-01",
            dropLocationCode: "WH-CNX-01",
            itemSeq: 1,
            itemId: "ITEM-001",
            description: "Test pallet",
            loadUnitProfileCode: null,
            dimensions: null,
            weightKg: 100,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
    }
}
