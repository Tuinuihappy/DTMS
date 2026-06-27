using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
// Alias avoids the "DeliveryOrder is namespace" ambiguity — our test
// namespace and the aggregate type share the segment.
using DeliveryOrderAggregate = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// Phase 2.5 Path A — verifies BuildWarehouseMapAsync (the new mode-aware
// validation path for Manual / Fleet orders). The AMR path
// (BuildStationMapAsync) has the same shape and its tests still cover it;
// these tests pin the warehouse-side semantics specifically:
//   - "code not found in warehouses" → Failure (not a partial / silent map)
//   - "warehouse deactivated" → Failure (same hard-stop as inactive station)
//   - Deduped + case-insensitive lookup (mixed-case operator input)
public class StationValidationServiceWarehouseTests
{
    private readonly IStationLookup _stationLookup = Substitute.For<IStationLookup>();
    private readonly IWarehouseLookup _warehouseLookup = Substitute.For<IWarehouseLookup>();
    private readonly StationValidationService _sut;

    public StationValidationServiceWarehouseTests()
    {
        _sut = new StationValidationService(_stationLookup, _warehouseLookup);
    }

    [Fact]
    public async Task BuildWarehouseMapAsync_AllCodesResolveAndActive_ReturnsMap()
    {
        var items = new[]
        {
            NewItem("WH-BKK-01", "WH-CNX-01"),
            NewItem("WH-BKK-01", "WH-KKC-01"),  // pickup dup intentionally
        };
        var bangkokId = Guid.NewGuid();
        var chiangmaiId = Guid.NewGuid();
        var khonkaenId = Guid.NewGuid();
        _warehouseLookup.ResolveBatchAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, WarehouseLookupResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["WH-BKK-01"] = new(bangkokId, "WH-BKK-01", "Bangkok DC", IsActive: true),
                ["WH-CNX-01"] = new(chiangmaiId, "WH-CNX-01", "Chiang Mai DC", IsActive: true),
                ["WH-KKC-01"] = new(khonkaenId, "WH-KKC-01", "Khon Kaen DC", IsActive: true),
            });

        var result = await _sut.BuildWarehouseMapAsync(items, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value["WH-BKK-01"].Should().Be(bangkokId);
        result.Value["WH-CNX-01"].Should().Be(chiangmaiId);
        result.Value["WH-KKC-01"].Should().Be(khonkaenId);
    }

    [Fact]
    public async Task BuildWarehouseMapAsync_CodeNotFound_ReturnsFailure()
    {
        // Silent partial maps would let the order land in a half-validated
        // state; this hard-fail at intake prevents that.
        var items = new[] { NewItem("WH-UNKNOWN", "WH-CNX-01") };
        _warehouseLookup.ResolveBatchAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, WarehouseLookupResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["WH-CNX-01"] = new(Guid.NewGuid(), "WH-CNX-01", "Chiang Mai DC", IsActive: true),
                // WH-UNKNOWN intentionally absent
            });

        var result = await _sut.BuildWarehouseMapAsync(items, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("WH-UNKNOWN");
        result.Error.Should().Contain("not a valid warehouse code");
    }

    [Fact]
    public async Task BuildWarehouseMapAsync_WarehouseDeactivated_ReturnsFailure()
    {
        // Soft-deleted warehouses must reject new orders (consistent with
        // station rejection behaviour). In-flight trips that already
        // reference the warehouse continue to work — only new submissions
        // are blocked.
        var items = new[] { NewItem("WH-BKK-01", "WH-BKK-01") };
        _warehouseLookup.ResolveBatchAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, WarehouseLookupResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["WH-BKK-01"] = new(Guid.NewGuid(), "WH-BKK-01", "Bangkok DC", IsActive: false),
            });

        var result = await _sut.BuildWarehouseMapAsync(items, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("WH-BKK-01");
        result.Error.Should().Contain("deactivated");
    }

    [Fact]
    public async Task BuildWarehouseMapAsync_NoItems_ReturnsEmptyMap()
    {
        _warehouseLookup.ResolveBatchAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, WarehouseLookupResult>());

        var result = await _sut.BuildWarehouseMapAsync(
            Array.Empty<Item>(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildWarehouseMapAsync_DedupesCodesBeforeLookup()
    {
        // 3 items with overlapping codes → only 2 distinct codes go to
        // the lookup. The Item-level loop emits each side separately
        // (Pickup + Drop = 6 total references), but the lookup gets 2.
        var items = new[]
        {
            NewItem("WH-BKK-01", "WH-CNX-01"),
            NewItem("WH-BKK-01", "WH-CNX-01"),
            NewItem("WH-BKK-01", "WH-CNX-01"),
        };
        _warehouseLookup.ResolveBatchAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, WarehouseLookupResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["WH-BKK-01"] = new(Guid.NewGuid(), "WH-BKK-01", "Bangkok DC", IsActive: true),
                ["WH-CNX-01"] = new(Guid.NewGuid(), "WH-CNX-01", "Chiang Mai DC", IsActive: true),
            });

        await _sut.BuildWarehouseMapAsync(items, CancellationToken.None);

        await _warehouseLookup.Received(1).ResolveBatchAsync(
            Arg.Is<IReadOnlyList<string>>(l => l.Count == 2),
            Arg.Any<CancellationToken>());
    }

    private static Item NewItem(string pickup, string drop) =>
        new Item(
            deliveryOrderId: Guid.NewGuid(),
            pickupLocationCode: pickup,
            dropLocationCode: drop,
            itemSeq: 1,
            itemId: "ITEM-001",
            description: null,
            loadUnitProfileCode: null,
            dimensions: null,
            weightKg: 10,
            quantity: Quantity.Create(1, UnitOfMeasure.EA));
}

// Note: aggregate-level MarkAsValidated(stationMap, warehouseMap) tests
// deferred — the DeliveryOrder.Create / AddItem APIs have a complex
// signature (Priority + ServiceWindow + SourceSystem types) that's not
// worth replicating in a unit test helper just to exercise a 5-line
// dictionary-lookup path. The same code path is covered by:
//   - StationValidationServiceWarehouseTests above (the map gets built right)
//   - ItemWarehouseIdTests (Phase 2.5 — SetWarehouseIds works correctly)
//   - the existing single-arg MarkAsValidated tests (loop + per-item dispatch)
// If a behavioural regression appears here we add an integration test
// against the running API instead of fighting the aggregate's factory.
