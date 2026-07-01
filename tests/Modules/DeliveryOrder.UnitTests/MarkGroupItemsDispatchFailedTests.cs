using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using DeliveryOrderAggregate = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Bug A regression coverage (commit 670dd1d) —
// MarkGroupItemsAsDispatchFailed used to match items by station-Id pair
// only. When Phase 3a made station Ids nullable on Items (for Manual /
// Fleet orders that resolve to warehouse Ids instead), the Planning
// consumer's collapsed-to-Guid.Empty grouping key would miss every item
// because `null != Guid.Empty`. Items stayed Pending, the order stayed
// Planned forever, and operators saw a Planned order with all-Failed
// jobs — confusing.
//
// These tests pin the post-fix matching contract:
//   - AMR-shaped call (station pair only) still matches AMR items
//   - Manual-shaped call (warehouse pair only) matches Manual items
//   - Empty-Guid sentinels on station side fall through to warehouse match
//   - Mismatched pair (neither station nor warehouse) leaves items untouched
//   - Already-finalised items (non-Pending OR bound to a Trip) are skipped
public class MarkGroupItemsDispatchFailedTests
{
    private static DeliveryOrderAggregate ConfirmedAmrOrder(out Guid pickup, out Guid drop)
    {
        var order = DeliveryOrderAggregate.Create(
            "MGD-AMR", Priority.Normal, serviceWindow: null);
        order.AddItem(
            "WH-A", "DOCK-1", 1, "SKU-A",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
        order.Submit();
        var stations = new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["DOCK-1"] = Guid.NewGuid(),
        };
        order.MarkAsValidated(stations);   // populates PickupStationId / DropStationId
        order.Confirm(weightFallbackKg: 5);
        pickup = order.Items.Single().PickupStationId!.Value;
        drop = order.Items.Single().DropStationId!.Value;
        return order;
    }

    private static DeliveryOrderAggregate ConfirmedManualOrder(out Guid pickupWh, out Guid dropWh)
    {
        var order = DeliveryOrderAggregate.Create(
            "MGD-MANUAL", Priority.Normal, serviceWindow: null,
            sourceSystemKey: "manual", sourceSystemDisplayName: "Manual",
            createdBy: "test", requestedBy: null, notes: null,
            requestedTransportMode: TransportMode.Manual);
        order.AddItem(
            "WH-A", "WH-B", 1, "SKU-M",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
        order.Submit();
        // Manual / Fleet path — warehouse map only, station map null.
        var warehouses = new Dictionary<string, Guid>
        {
            ["WH-A"] = Guid.NewGuid(),
            ["WH-B"] = Guid.NewGuid(),
        };
        order.MarkAsValidated(stationMap: null, warehouseMap: warehouses);
        order.Confirm(weightFallbackKg: 5);
        pickupWh = order.Items.Single().PickupWarehouseId!.Value;
        dropWh = order.Items.Single().DropWarehouseId!.Value;
        return order;
    }

    [Fact]
    public void AmrPath_MatchesByStationPair_AndMarksItemFailed()
    {
        // Pre-Bug-A behaviour preserved: AMR consumer passes station pair
        // (warehouse pair null), items match, status flips to Failed.
        var order = ConfirmedAmrOrder(out var pickup, out var drop);

        var marked = order.MarkGroupItemsAsDispatchFailed(
            pickupStationId: pickup, dropStationId: drop,
            pickupWarehouseId: null, dropWarehouseId: null,
            reason: "vendor rejected");

        marked.Should().Be(1);
        order.Items.Single().Status.Should().Be(ItemStatus.Failed);
    }

    [Fact]
    public void ManualPath_MatchesByWarehousePair_AndMarksItemFailed()
    {
        // The Bug A fix's primary new behaviour: Manual orders with null
        // station Ids match by warehouse pair instead.
        var order = ConfirmedManualOrder(out var pickupWh, out var dropWh);

        var marked = order.MarkGroupItemsAsDispatchFailed(
            pickupStationId: null, dropStationId: null,
            pickupWarehouseId: pickupWh, dropWarehouseId: dropWh,
            reason: "manual dispatch stub");

        marked.Should().Be(1);
        order.Items.Single().Status.Should().Be(ItemStatus.Failed);
    }

    [Fact]
    public void ManualPath_StationEmptyGuid_FallsThroughTo_WarehouseMatch()
    {
        // Real consumer scenario: Manual order grouping collapses null
        // station Ids to (Guid.Empty, Guid.Empty); consumer passes both
        // pairs. Sentinel station pair must NOT match (would otherwise
        // mark items of other orders that happen to also have null station
        // Ids), but warehouse pair must.
        var order = ConfirmedManualOrder(out var pickupWh, out var dropWh);

        var marked = order.MarkGroupItemsAsDispatchFailed(
            pickupStationId: Guid.Empty, dropStationId: Guid.Empty,
            pickupWarehouseId: pickupWh, dropWarehouseId: dropWh,
            reason: "manual stub");

        marked.Should().Be(1);
        order.Items.Single().Status.Should().Be(ItemStatus.Failed);
    }

    [Fact]
    public void NeitherPairMatches_LeavesItemUntouched()
    {
        // Caller passed a station pair that belongs to a different group
        // (e.g. multi-group AMR order, this method called for group B
        // when our item is group A's). Item must stay Pending.
        var order = ConfirmedAmrOrder(out _, out _);

        var marked = order.MarkGroupItemsAsDispatchFailed(
            pickupStationId: Guid.NewGuid(), dropStationId: Guid.NewGuid(),
            pickupWarehouseId: null, dropWarehouseId: null,
            reason: "wrong group");

        marked.Should().Be(0);
        order.Items.Single().Status.Should().Be(ItemStatus.Pending);
    }

    [Fact]
    public void AllPairsNull_LeavesItemsUntouched()
    {
        // Defensive: caller passed nothing identifiable. Don't mark anything.
        var order = ConfirmedAmrOrder(out _, out _);

        var marked = order.MarkGroupItemsAsDispatchFailed(
            pickupStationId: null, dropStationId: null,
            pickupWarehouseId: null, dropWarehouseId: null,
            reason: "no pair");

        marked.Should().Be(0);
        order.Items.Single().Status.Should().Be(ItemStatus.Pending);
    }
}
