using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.ReviseAndResendDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

/// <summary>
/// Edit &amp; resend refuses a revision whose route has no usable order
/// template — synchronously, before anything is saved — instead of letting
/// Planning fail it again a few seconds later.
/// </summary>
public class ReviseAndResendHandlerTests
{
    private static readonly Dictionary<string, Guid> Stations = new()
    {
        ["ST-A"] = Guid.NewGuid(),
        ["ST-B"] = Guid.NewGuid(),
        ["ST-C"] = Guid.NewGuid(),
    };

    [Fact]
    public async Task RouteWithoutTemplate_Throws422Exception_NamingTheRoute_NothingSaved()
    {
        var (handler, repo, strategy) = Build();
        var order = FailedOrder();
        repo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        strategy.CheckRoutesAsync(Arg.Any<IReadOnlyCollection<(Guid, Guid)>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<(Guid Pickup, Guid Drop)>)ci[0])
                .Select(r => new RouteProblem(r.Pickup, r.Drop, "no active OrderTemplate is registered for this route"))
                .ToList());

        var act = () => handler.Handle(Command(order, pickup: "ST-C"), CancellationToken.None);

        (await act.Should().ThrowAsync<RouteNotDispatchableException>())
            .Which.Message.Should().Contain("ST-C → ST-B");
        order.Status.Should().Be(OrderStatus.Failed);
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchableRoute_SavesAndConfirms()
    {
        var (handler, repo, strategy) = Build();
        var order = FailedOrder();
        repo.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        strategy.CheckRoutesAsync(Arg.Any<IReadOnlyCollection<(Guid, Guid)>>(), Arg.Any<CancellationToken>())
            .Returns(new List<RouteProblem>());

        var result = await handler.Handle(Command(order, pickup: "ST-C"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.Items.Single().PickupStationId.Should().Be(Stations["ST-C"]);
        var checkedRoutes = strategy.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IDispatchStrategy.CheckRoutesAsync))
            .GetArguments()[0] as IReadOnlyCollection<(Guid Pickup, Guid Drop)>;
        checkedRoutes.Should().Equal((Stations["ST-C"], Stations["ST-B"]));
        await repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static (ReviseAndResendDeliveryOrderCommandHandler handler,
        IDeliveryOrderRepository repo,
        IDispatchStrategy strategy) Build()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();

        var stations = Substitute.For<IStationValidationService>();
        stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(Stations));

        var strategy = Substitute.For<IDispatchStrategy>();
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(TransportMode.Amr).Returns(true);
        registry.Get(TransportMode.Amr).Returns(strategy);

        var trips = Substitute.For<ITripRepository>();
        trips.GetByDeliveryOrderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trip>());

        var user = Substitute.For<ICurrentUserAccessor>();
        user.GetCurrentUserName().Returns("ops-1");

        var handler = new ReviseAndResendDeliveryOrderCommandHandler(
            repo,
            Substitute.For<IOrderAmendmentRepository>(),
            Substitute.For<IOrderAuditEventRepository>(),
            trips, stations, registry, new RouteDispatchGuard(registry), user, new DTMS.DeliveryOrder.Application.Diagnostics.OrderIntakeMetrics(),
            Options.Create(new DeliveryOrderOptions()),
            NullLogger<ReviseAndResendDeliveryOrderCommandHandler>.Instance);
        return (handler, repo, strategy);
    }

    private static DomainOrder FailedOrder()
    {
        var order = DomainOrder.Create("REV-H", Priority.Normal, serviceWindow: null);
        order.AddItem("ST-A", "ST-B", 1, "SKU-1", null, null, null, 2.0, Quantity.Create(1, "EA"));
        order.Submit();
        order.MarkAsValidated(Stations);
        order.Confirm(weightFallbackKg: 500);
        var trip = Guid.NewGuid();
        order.AssignItemsToTrip(trip, 1, Stations["ST-A"], Stations["ST-B"]);
        order.MarkTripItemsFailed(trip, "vendor rejected");
        order.RecomputeStatusFromItems();
        order.ClearDomainEvents();
        return order;
    }

    private static ReviseAndResendDeliveryOrderCommand Command(DomainOrder order, string pickup)
    {
        var item = order.Items.Single();
        return new ReviseAndResendDeliveryOrderCommand(
            order.Id, "wrong pickup station", Priority.High,
            new ServiceWindowDto(DateTime.UtcNow, DateTime.UtcNow.AddHours(2)),
            [new RevisedItemDto(item.Id, item.ItemId, null, pickup, "ST-B", null, null, 2.0, new QuantityDto(1, "EA"))]);
    }
}
