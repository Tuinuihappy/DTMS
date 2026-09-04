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
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using DomainOrder = DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder;

namespace DeliveryOrder.UnitTests;

// OrderRef is a business reference, not an idempotency key: reusing one is a
// conflict on both create paths. Retry safety lives in the Idempotency-Key
// filter, which replays at the endpoint before the handler is reached — so no
// handler here needs a replay rule of its own.
//
// The database's UNIQUE (SourceSystemKey, OrderRef) index is the guarantee;
// these cover the handler pre-checks that turn it into a message naming the
// ref that collided, plus the race path where the pre-check reads too early.
public class DuplicateOrderRefTests
{
    [Fact]
    public async Task Upstream_ExistingRef_ThrowsDuplicateValue()
    {
        var (handler, repo, _) = BuildUpstream();
        StubExisting(repo);

        var act = () => handler.Handle(UpstreamCommand(), CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateValueException>())
            .Which.Message.Should().Contain("OD-DUP-01");
        await repo.DidNotReceive().AddAsync(Arg.Any<DomainOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upstream_ExistingRef_RejectsEvenWhenPayloadIsIdentical()
    {
        // A byte-identical repeat is still a conflict here. If it were a real
        // retry the Idempotency-Key filter would have replayed the original
        // response and this handler would never have run.
        var (handler, repo, _) = BuildUpstream();
        StubExisting(repo);

        var act = () => handler.Handle(UpstreamCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateValueException>();
    }

    [Fact]
    public async Task Upstream_RefTakenByConcurrentInsert_ThrowsDuplicateValue()
    {
        // Race: the pre-check saw nothing, the insert lost to another writer.
        // The re-query finds the winner, so the 409 can still name the ref
        // instead of surfacing a raw unique-violation.
        var (handler, repo, stations) = BuildUpstream();
        StationsSucceed(stations);
        repo.GetByRefAsync("oms", "OD-DUP-01", Arg.Any<CancellationToken>())
            .Returns(_ => (DomainOrder?)null, _ => ExistingOrder());
        repo.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new Microsoft.EntityFrameworkCore.DbUpdateException(
                "23505", (Exception?)null));

        var act = () => handler.Handle(UpstreamCommand(), CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateValueException>())
            .Which.Message.Should().Contain("OD-DUP-01");
    }

    [Fact]
    public async Task Draft_DuplicateRef_ThrowsDuplicateValueNamingTheRef()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var existing = DomainOrder.Create(
            "OD-DUP-01", Priority.Normal,
            ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)));
        repo.GetByRefAsync("internal", "OD-DUP-01", Arg.Any<CancellationToken>()).Returns(existing);

        var uom = Substitute.For<IUomNormalizer>();
        uom.Normalize(Arg.Any<string?>()).Returns(UnitOfMeasure.EA);
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetInternalAsync(Arg.Any<CancellationToken>())
            .Returns(new OrderOrigin("internal", "Internal"));

        var handler = new CreateDraftDeliveryOrderCommandHandler(
            repo, uom, Substitute.For<ICurrentUserAccessor>(), origins,
            NullLogger<CreateDraftDeliveryOrderCommandHandler>.Instance);

        var act = () => handler.Handle(new CreateDraftDeliveryOrderCommand(
            "OD-DUP-01",
            new ServiceWindowDto(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
            [Item()]), CancellationToken.None);

        (await act.Should().ThrowAsync<DuplicateValueException>())
            .Which.Message.Should().Contain("OD-DUP-01");
        await repo.DidNotReceive().AddAsync(Arg.Any<DomainOrder>(), Arg.Any<CancellationToken>());
    }

    // ── Fixtures ────────────────────────────────────────────────────────

    private static DomainOrder ExistingOrder() => DomainOrder.CreateFromUpstream(
        "OD-DUP-01", Priority.Normal,
        ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)),
        "oms", "OMS", "OMS", null, null, TransportMode.Amr, false);

    private static void StubExisting(IDeliveryOrderRepository repo)
    {
        var existing = ExistingOrder();
        repo.GetByRefAsync("oms", "OD-DUP-01", Arg.Any<CancellationToken>()).Returns(existing);
        repo.GetByIdAsNoTrackingAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
    }

    private static void StationsSucceed(IStationValidationService stations)
        => stations.BuildStationMapAsync(Arg.Any<IEnumerable<Item>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, Guid>>.Success(
                new Dictionary<string, Guid> { ["LOC-A"] = Guid.NewGuid(), ["LOC-B"] = Guid.NewGuid() }));

    private static (CreateUpstreamDeliveryOrderCommandHandler handler,
        IDeliveryOrderRepository repo,
        IStationValidationService stations) BuildUpstream()
    {
        var repo = Substitute.For<IDeliveryOrderRepository>();
        var stations = Substitute.For<IStationValidationService>();
        var uom = Substitute.For<IUomNormalizer>();
        uom.Normalize(Arg.Any<string?>()).Returns(UnitOfMeasure.EA);
        var origins = Substitute.For<IOrderOriginResolver>();
        origins.GetByKeyAsync("oms", Arg.Any<CancellationToken>())
            .Returns(new OrderOrigin("oms", "OMS"));
        var registry = Substitute.For<IDispatchStrategyRegistry>();
        registry.IsRegistered(Arg.Any<TransportMode>()).Returns(true);

        var handler = new CreateUpstreamDeliveryOrderCommandHandler(
            repo, Substitute.For<IOrderAuditEventRepository>(),
            Substitute.For<IOrderActivityProjectionStore>(),
            stations, uom,
            Substitute.For<ICurrentUserAccessor>(), origins, registry,
            Options.Create(new DeliveryOrderOptions()),
            NullLogger<CreateUpstreamDeliveryOrderCommandHandler>.Instance);
        return (handler, repo, stations);
    }

    private static CreateUpstreamDeliveryOrderCommand UpstreamCommand() => new(
        "OD-DUP-01",
        new ServiceWindowDto(
            new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc)),
        [Item()],
        SourceSystemKey: "oms");

    private static ItemDto Item() => new(
        ItemId: "SKU-1", Description: null,
        PickupLocationCode: "LOC-A", DropLocationCode: "LOC-B",
        LoadUnitProfileCode: null, Dimensions: null, WeightKg: 1.0,
        Quantity: new QuantityDto(1, "EA"));
}
