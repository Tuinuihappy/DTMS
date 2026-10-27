using DTMS.Facility.Application.Services;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.ValueObjects;
using DTMS.Facility.Infrastructure.Data;
using DTMS.Facility.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Facility.UnitTests;

// Station resolution against a real FacilityDbContext (EF InMemory). Deleting
// a map leaves its Stations rows behind, and a re-imported map reuses RIOT3's
// numeric station ids — so both resolvers must ignore stations whose MapId no
// longer exists in Maps (2026-08-05: 13 duplicate VendorRefs in prod, e.g.
// 177 = live SHELF_FG_1 + orphaned SHELF10).
public class FacilityReadServiceTests
{
    private static FacilityDbContext NewDb()
        => new(new DbContextOptionsBuilder<FacilityDbContext>()
            .UseInMemoryDatabase("facility-" + Guid.NewGuid()).Options);

    private static Station NewStation(Guid mapId, string code, string vendorRef)
    {
        var station = new Station(Guid.NewGuid(), mapId, code, new Coordinate(0, 0), StationType.Normal);
        station.SetCode(code);
        station.SetVendorRef(vendorRef);
        return station;
    }

    [Fact]
    public async Task ResolveByVendorRef_DuplicateAcrossDeletedMap_ReturnsLiveMapStation()
    {
        await using var db = NewDb();
        var liveMap = new Map(Guid.NewGuid(), "FAN1_MAP", "v2", 100, 100, "{}");
        db.Maps.Add(liveMap);

        // Orphan inserted FIRST so an unguarded FirstOrDefault would find it.
        var orphan = NewStation(mapId: Guid.NewGuid(), "SHELF10", vendorRef: "177");
        var live = NewStation(liveMap.Id, "SHELF_FG_1", vendorRef: "177");
        db.Stations.AddRange(orphan, live);
        await db.SaveChangesAsync();

        var resolved = await new FacilityReadService(db)
            .ResolveStationByVendorRefAsync("177");

        resolved.Should().Be(live.Id, "orphaned stations of a deleted map must never win resolution");
    }

    [Fact]
    public async Task ResolveByVendorRef_OnlyOrphanHasTheRef_ReturnsNull()
    {
        await using var db = NewDb();
        db.Maps.Add(new Map(Guid.NewGuid(), "FAN1_MAP", "v2", 100, 100, "{}"));
        db.Stations.Add(NewStation(mapId: Guid.NewGuid(), "SHELF10", vendorRef: "177"));
        await db.SaveChangesAsync();

        var resolved = await new FacilityReadService(db)
            .ResolveStationByVendorRefAsync("177");

        resolved.Should().BeNull("a ref that only exists on an orphaned station must not resolve");
    }

    [Fact]
    public async Task ResolveByVendorRef_UnknownRef_ReturnsNull()
    {
        await using var db = NewDb();
        var map = new Map(Guid.NewGuid(), "FAN1_MAP", "v2", 100, 100, "{}");
        db.Maps.Add(map);
        db.Stations.Add(NewStation(map.Id, "SHELF_FG_1", vendorRef: "177"));
        await db.SaveChangesAsync();

        var resolved = await new FacilityReadService(db)
            .ResolveStationByVendorRefAsync("999");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task FindUnavailableVendorStations_ReportsRemovedAndMissing_IgnoresUnsyncedMaps()
    {
        await using var db = NewDb();
        var map = new Map(Guid.NewGuid(), "FAN1_MAP", "v2", 100, 100, "{}");
        map.SetVendorRef("2");
        db.Maps.Add(map);
        var removed = NewStation(map.Id, "ATH_01", vendorRef: "119");
        removed.Deactivate();
        db.Stations.AddRange(
            NewStation(map.Id, "SHELF1", vendorRef: "5"),
            removed,
            // Same ref on an orphaned row must not make 777 look known.
            NewStation(mapId: Guid.NewGuid(), "ORPHAN", vendorRef: "777"));
        await db.SaveChangesAsync();

        var result = await new FacilityReadService(db).FindUnavailableVendorStationsAsync(
        [
            new VendorStationRef("2", "5"),     // live
            new VendorStationRef("2", "119"),   // removed by RIOT3
            new VendorStationRef("2", "777"),   // synced map, no such station
            new VendorStationRef("9", "1"),     // map DTMS doesn't sync
        ]);

        result.Should().BeEquivalentTo(new[]
        {
            new UnavailableVendorStation("2", "119", "ATH_01"),
            new UnavailableVendorStation("2", "777", null),
        });
    }
}
