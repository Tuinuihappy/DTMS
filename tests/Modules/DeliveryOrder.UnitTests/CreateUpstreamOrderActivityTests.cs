using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Application.Options;
using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// The upstream-notification panel keys its visibility off an
// OrderUpstreamIngested row in the OrderActivity timeline. The projector
// can't produce it (no integration event for ingest), so the create handler
// mirrors it directly — these tests pin that write to exactly the success
// path: never on the idempotent-duplicate return, never on validation
// failure.
public class CreateUpstreamOrderActivityTests
{
    private static (CreateUpstreamDeliveryOrderCommandHandler handler,
        IDeliveryOrderRepository repo,
        IOrderActivityProjectionStore activity,
        IStationValidationService stations) Build()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var audit = Substitute.For<IOrderAuditEventRepository>();
        var activity = Substitute.For<IOrderActivityProjectionStore>();
        var stations = Substitute.For<IStationValidationService>();
        var uom = Substitute.For<IUomNormalizer>();
        uom.Normalize(Arg.Any<string?>()).Returns(UnitOfMeasure.EA);
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetByKeyAsync("oms", Arg.Any<CancellationToken>())
            .Returns(new OrderOrigin("oms", "OMS"));

        var handler = new CreateUpstreamDeliveryOrderCommandHandler(
            repo, audit, activity, stations, uom,
            Substitute.For<ICurrentUserAccessor>(), origins,
            Options.Create(new DeliveryOrderOptions()),
            NullLogger<CreateUpstreamDeliveryOrderCommandHandler>.Instance);
        return (handler, repo, activity, stations);
    }

    private static CreateUpstreamDeliveryOrderCommand Command(string orderRef = "OD-TEST-01") => new(
        orderRef,
        new ServiceWindowDto(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
        [
            new ItemDto(
                ItemId: "SKU-1", Description: null,
                PickupLocationCode: "LOC-A", DropLocationCode: "LOC-B",
                LoadUnitProfileCode: null, Dimensions: null, WeightKg: 1.0,
                Quantity: new QuantityDto(1, "EA")),
        ],
        SourceSystemKey: "oms");

    private static void StationsSucceed(IStationValidationService stations)
        => stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(
                new Dictionary<string, Guid> { ["LOC-A"] = Guid.NewGuid(), ["LOC-B"] = Guid.NewGuid() }));

    [Fact]
    public async Task Handle_SuccessfulIngest_MirrorsIngestedRowIntoOrderActivity()
    {
        var (handler, _, activity, stations) = Build();
        StationsSucceed(stations);

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 4th/5th positional args are category/eventType; trailing named arg
        // is systemKey — mirrors the SourceCallbackOutcomeConsumer tests.
        await activity.Received(1).AppendAsync(
            CreateUpstreamDeliveryOrderCommandHandler.IngestActivityProjectorName,
            Arg.Any<Guid>(), Arg.Any<Guid>(),
            "OrderLifecycle", "OrderUpstreamIngested",
            Arg.Is<string>(d => d!.Contains("OD-TEST-01")), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<Guid?>(), Arg.Any<int?>(),
            Arg.Any<CancellationToken>(), Arg.Any<string>(), Arg.Any<string>(),
            systemKey: "oms");
    }

    [Fact]
    public async Task Handle_DuplicateOrderRef_ReturnsExisting_WithoutActivityWrite()
    {
        var (handler, repo, activity, _) = Build();
        var existing = DomainOrder.CreateFromUpstream(
            "OD-TEST-01", Priority.Normal,
            ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
            "oms", "OMS", "OMS", null, null, TransportMode.Amr, false);
        repo.GetByRefAsync("oms", "OD-TEST-01", Arg.Any<CancellationToken>()).Returns(existing);
        repo.GetByIdAsNoTrackingAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await activity.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default!, default!, default, default,
            default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Handle_StationValidationFails_NoActivityWrite()
    {
        var (handler, _, activity, stations) = Build();
        stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Failure("unknown station 'LOC-A'"));

        var result = await handler.Handle(Command(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await activity.DidNotReceiveWithAnyArgs().AppendAsync(
            default!, default, default, default!, default!, default, default,
            default, default, default, default, default, default, default);
    }
}
