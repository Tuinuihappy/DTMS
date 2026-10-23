using DTMS.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;
using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Application.Services;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

/// <summary>
/// An order whose route has no usable order template is refused when it
/// arrives — 422 for the source system, a per-order failure in a bulk
/// submit — instead of being accepted and failing in Planning seconds later.
/// </summary>
public class RouteDispatchGuardTests
{
    private static readonly Dictionary<string, Guid> Stations = new()
    {
        ["SHELF3"] = Guid.NewGuid(),
        ["SHELF5"] = Guid.NewGuid(),
        ["PACK-1"] = Guid.NewGuid(),
    };

    /// <summary>A strategy for which only routes from <paramref name="badPickup"/> fail.</summary>
    private static IDispatchStrategyRegistry Registry(string badPickup = "SHELF3")
    {
        var strategy = Substitute.For<IDispatchStrategy>();
        strategy.CheckRoutesAsync(Arg.Any<IReadOnlyCollection<(Guid, Guid)>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<(Guid Pickup, Guid Drop)>)ci[0])
                .Where(r => r.Pickup == Stations[badPickup])
                .Select(r => new RouteProblem(r.Pickup, r.Drop, "no active OrderTemplate is registered for this route"))
                .ToList());
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(Arg.Any<TransportMode>()).Returns(true);
        registry.Get(TransportMode.Amr).Returns(strategy);
        return registry;
    }

    private static List<Item> ItemsFrom(params string[] pickups)
    {
        var order = DomainOrder.Create("G", Priority.Normal, serviceWindow: null);
        foreach (var (pickup, i) in pickups.Select((p, i) => (p, i + 1)))
            order.AddItem(pickup, "PACK-1", i, $"SKU-{i}", null, null, null, 1.0, Quantity.Create(1, "EA"));
        return order.Items.ToList();
    }

    [Fact]
    public async Task OneBadRouteOfTwo_NamesOnlyTheBadRoute()
    {
        var guard = new RouteDispatchGuard(Registry());

        var problem = await guard.CheckAsync("OD-1", ItemsFrom("SHELF3", "SHELF5"), TransportMode.Amr, Stations);

        problem!.Message.Should().Contain("OD-1").And.Contain("SHELF3 → PACK-1").And.NotContain("SHELF5");
        problem.Routes.Should().Equal("SHELF3 → PACK-1");
    }

    [Fact]
    public async Task AllRoutesFine_NoProblem()
    {
        var guard = new RouteDispatchGuard(Registry());

        (await guard.CheckAsync("OD-1", ItemsFrom("SHELF5"), TransportMode.Amr, Stations)).Should().BeNull();
    }

    [Fact]
    public async Task NoStationMap_ManualOrders_AreNotChecked()
    {
        var registry = Registry();
        var guard = new RouteDispatchGuard(registry);

        (await guard.CheckAsync("OD-1", ItemsFrom("SHELF3"), TransportMode.Manual, stationMap: null))
            .Should().BeNull();
        registry.DidNotReceive().Get(Arg.Any<TransportMode>());
    }

    [Fact]
    public async Task SourceSystemIntake_RouteWithoutTemplate_Is422_AndNothingIsSaved()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetByKeyAsync("oms", Arg.Any<CancellationToken>()).Returns(new OrderOrigin("oms", "OMS"));
        var stations = Substitute.For<IStationValidationService>();
        stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(Stations));
        var registry = Registry();
        var handler = new CreateUpstreamDeliveryOrderCommandHandler(
            repo, Substitute.For<IOrderAuditEventRepository>(), activity, stations,
            Substitute.For<ICurrentUserAccessor>(), origins, registry, new RouteDispatchGuard(registry), new DTMS.DeliveryOrder.Application.Diagnostics.OrderIntakeMetrics(),
            Options.Create(new DeliveryOrderOptions()),
            NullLogger<CreateUpstreamDeliveryOrderCommandHandler>.Instance);

        var act = () => handler.Handle(new CreateUpstreamDeliveryOrderCommand(
            "OD-221026-0191",
            new ServiceWindowDto(DateTime.UtcNow, DateTime.UtcNow.AddHours(2)),
            [new ItemDto("LOT-1", null, "SHELF3", "PACK-1", null, null, 1.0, new QuantityDto(1, "EA"))],
            SourceSystemKey: "oms",
            RequestedTransportMode: TransportMode.Amr), CancellationToken.None);

        (await act.Should().ThrowAsync<RouteNotDispatchableException>())
            .Which.Message.Should().Contain("SHELF3 → PACK-1");
        await repo.DidNotReceive().AddAsync(Arg.Any<DomainOrder>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkSubmit_BadRouteFailsThatOrderOnly()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var stations = Substitute.For<IStationValidationService>();
        stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(Stations));
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetInternalAsync(Arg.Any<CancellationToken>()).Returns(new OrderOrigin("internal", "Internal"));
        var registry = Registry();
        var handler = new BulkSubmitDeliveryOrdersCommandHandler(
            repo, stations, Substitute.For<ICurrentUserAccessor>(), origins, registry,
            new RouteDispatchGuard(registry), new DTMS.DeliveryOrder.Application.Diagnostics.OrderIntakeMetrics(), Options.Create(new DeliveryOrderOptions()));

        CreateDraftDeliveryOrderCommand Order(string orderRef, string pickup) => new(
            orderRef,
            new ServiceWindowDto(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
            [new ItemDto("SKU-1", null, pickup, "PACK-1", null, null, 1.0, new QuantityDto(1, "EA"))],
            RequestedTransportMode: TransportMode.Amr);

        var result = await handler.Handle(
            new BulkSubmitDeliveryOrdersCommand([Order("OK-1", "SHELF5"), Order("BAD-1", "SHELF3")]),
            CancellationToken.None);

        result.Value.Succeeded.Should().ContainSingle().Which.Order.OrderRef.Should().Be("OK-1");
        result.Value.Failures.Should().ContainSingle()
            .Which.Should().Match<BulkSubmitFailure>(f => f.OrderRef == "BAD-1" && f.Reason.Contains("SHELF3 → PACK-1"));
    }
}
