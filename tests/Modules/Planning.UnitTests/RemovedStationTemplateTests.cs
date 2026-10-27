using DTMS.Planning.Application.Queries.GetOrderTemplates;
using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Planning.UnitTests;

// Templates that route through or MOVE to a station RIOT3 removed are
// flagged (RemovedStations) and can be listed on their own (NeedsFix).
public class RemovedStationTemplateTests
{
    private static readonly Guid Ath01 = Guid.NewGuid();
    private static readonly Guid Shelf1 = Guid.NewGuid();
    private static readonly Guid Pack = Guid.NewGuid();
    private static readonly Guid Purged = Guid.NewGuid();

    // RIOT3 map 2 is synced. SHELF1 (5), PACK (10) live; ATH_01 (119) and
    // F_21 (189) removed; station 777 and Guid `Purged` are gone altogether.
    private static readonly RemovedStationSet Removed = new(
    [
        (Shelf1, 2, 5, "SHELF1", true),
        (Pack, 2, 10, "PACK", true),
        (Ath01, 2, 119, "ATH_01", false),
        (Guid.NewGuid(), 2, 189, "F_21", false),
    ]);

    private static OrderTemplate Template(string name, Guid? pickup, Guid? drop, params int[] moveStations)
        => new(
            name: name,
            priority: 10,
            structureType: "sequence",
            transportOrderPriority: 10,
            missions: moveStations.Select((s, i) => OrderTemplateMission.CreateMove(i + 1, "agv", 2, s)).ToArray(),
            pickupStationId: pickup,
            dropStationId: drop);

    [Fact]
    public void Find_ReportsRouteAndMoveHitsOnce_InOrder_IgnoringUnsyncedMaps()
    {
        var found = Removed.Find(Shelf1, Ath01, [(2, 5), (2, 119), (2, 189), (3, 119)]);

        found.Should().Equal("ATH_01", "F_21");
    }

    [Fact]
    public void Find_ReportsPurgedStations_ThatNoLongerHaveARow()
    {
        var found = Removed.Find(Purged, Pack, [(2, 777)]);

        found.Should().Equal("a deleted station", "RIOT3 station 777");
    }

    [Fact]
    public void Unknown_ReportsNothing()
        => RemovedStationSet.Unknown.Find(Purged, Ath01, [(2, 119)]).Should().BeEmpty();

    [Fact]
    public void Dto_CarriesRemovedStations_OrNullWhenClean()
    {
        var broken = OrderTemplateDtoFactory.From(Template("SHELF1 TO ATH01", Shelf1, Ath01, 5, 119), Removed);
        var clean = OrderTemplateDtoFactory.From(Template("SHELF1 TO PACK", Shelf1, Pack, 5, 10), Removed);

        broken.RemovedStations.Should().Equal("ATH_01");
        clean.RemovedStations.Should().BeNull();
    }

    [Fact]
    public async Task NeedsFix_ListsOnlyBrokenTemplates_PagedAfterFiltering()
    {
        var all = new List<OrderTemplate>
        {
            Template("A clean", Shelf1, Pack, 5),
            Template("B route", Shelf1, Ath01, 5),
            Template("C waypoint", Shelf1, Pack, 5, 189),
            Template("D clean", Shelf1, Pack, 10),
        };
        var repo = Substitute.For<IOrderTemplateRepository>();
        repo.ListPagedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<OrderTemplate>)all, (long)all.Count));

        var page1 = await new GetOrderTemplatesQueryHandler(repo)
            .Handle(new GetOrderTemplatesQuery(Page: 1, Size: 1, Removed: Removed, NeedsFix: true), CancellationToken.None);

        page1.Value.Total.Should().Be(2);
        page1.Value.Pages.Should().Be(2);
        page1.Value.Records.Should().ContainSingle(r => r.Name == "B route");
    }
}
