using System.Net;
using System.Text;
using DTMS.Facility.Application.Commands.PurgeStations;
using DTMS.Facility.Application.Commands.SyncMapStations;
using DTMS.Facility.Application.Services;
using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.ValueObjects;
using DTMS.Facility.Infrastructure.Data;
using DTMS.Facility.Infrastructure.Repositories;
using DTMS.Facility.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Facility.UnitTests;

// RIOT3 → Stations sync. EF InMemory doesn't enforce the (MapId, Code) unique
// index, so collision tests assert on the resulting rows instead of an exception.
public class SyncMapStationsHandlerTests
{
    private readonly FacilityDbContext _db = new(new DbContextOptionsBuilder<FacilityDbContext>()
        .UseInMemoryDatabase("sync-" + Guid.NewGuid()).Options);
    private readonly IRiot3FacilityClient _riot3 = Substitute.For<IRiot3FacilityClient>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly Map _map;

    public SyncMapStationsHandlerTests()
    {
        _map = new Map(Guid.NewGuid(), "FAN1_MAP", "v1", 100, 100, "{}");
        _map.SetVendorRef("2");
        _db.Maps.Add(_map);
        _db.SaveChanges();
    }

    private Station Seed(string name, string vendorRef, string? code = null, bool active = true,
        StationType type = StationType.Normal)
    {
        var s = new Station(Guid.NewGuid(), _map.Id, name, new Coordinate(1, 2, 0.003), type);
        s.SetVendorRef(vendorRef);
        s.SetCode(code ?? name);
        if (!active) s.Deactivate();
        _db.Stations.Add(s);
        _db.SaveChanges();
        return s;
    }

    private static Riot3StationInfo Riot(int id, string name, double x = 1, double y = 2)
        => new(id, name, x, y, 3);

    private void RiotReturns(params Riot3StationInfo[] stations)
        => _riot3.GetStationsAsync(2, Arg.Any<CancellationToken>()).Returns(stations.ToList());

    private Task<DTMS.SharedKernel.Messaging.Result<SyncMapStationsResult>> Sync(bool confirm = false)
        => new SyncMapStationsCommandHandler(
                new MapRepository(_db), new StationRepository(_db), _riot3, _cache,
                new StationSyncOptions(), NullLogger<SyncMapStationsCommandHandler>.Instance)
            .Handle(new SyncMapStationsCommand(_map.Id, confirm), CancellationToken.None);

    private List<Station> Rows() => _db.Stations.AsNoTracking().OrderBy(s => s.Name).ToList();

    [Fact]
    public async Task RecreatedStation_TakesOverRemovedRow_KeepingIdTypeAndCode()
    {
        var old = Seed("STF_21", "125", active: false, type: StationType.Dropoff);
        RiotReturns(Riot(300, "STF_21", x: 9));

        var result = await Sync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Rebound.Should().Be(1);
        result.Value.Added.Should().Be(0);
        var row = Rows().Should().ContainSingle().Subject;
        row.Id.Should().Be(old.Id);
        row.VendorRef.Should().Be("300");
        row.Code.Should().Be("STF_21");
        row.Type.Should().Be(StationType.Dropoff);
        row.IsActive.Should().BeTrue();
        row.Coordinate.X.Should().Be(9);
    }

    [Fact]
    public async Task RecreatedBetweenSyncs_ActiveRowIsTakenOverNotDeactivated()
    {
        var old = Seed("STF_21", "125");
        RiotReturns(Riot(300, "STF_21"));

        var result = await Sync();

        result.Value.Rebound.Should().Be(1);
        result.Value.Deactivated.Should().Be(0);
        Rows().Should().ContainSingle(s => s.Id == old.Id && s.VendorRef == "300" && s.IsActive);
    }

    [Fact]
    public async Task Takeover_MatchesRiotName_WhenOperatorEditedTheCode()
    {
        var old = Seed("PREFORM", "154", code: "PREFORM01", active: false);
        RiotReturns(Riot(400, "PREFORM"));

        var result = await Sync();

        result.Value.Rebound.Should().Be(1);
        var row = Rows().Should().ContainSingle().Subject;
        row.Id.Should().Be(old.Id);
        row.Code.Should().Be("PREFORM01");
        row.VendorRef.Should().Be("400");
    }

    [Fact]
    public async Task NewStation_WhoseCodeIsTakenByLiveStation_IsAddedWithoutCode()
    {
        Seed("DOCK_A", "10", code: "STF_99");
        RiotReturns(Riot(10, "DOCK_A"), Riot(11, "STF_99"));

        var result = await Sync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Added.Should().Be(1);
        result.Value.CodeConflicts.Should().Be(1);
        Rows().Single(s => s.VendorRef == "11").Code.Should().BeNull();
    }

    [Fact]
    public async Task TwoNewStationsWithTheSameName_SecondGetsNoCode()
    {
        RiotReturns(Riot(1, "DUP"), Riot(2, "dup"));

        var result = await Sync();

        result.Value.Added.Should().Be(2);
        result.Value.CodeConflicts.Should().Be(1);
        Rows().Count(s => s.Code == "DUP").Should().Be(1);
    }

    [Fact]
    public async Task EmptyRiotList_WithActiveStations_FailsWithoutDeactivating()
    {
        Seed("A", "1");
        Seed("B", "2");
        RiotReturns();

        var result = await Sync();

        result.IsFailure.Should().BeTrue();
        Rows().Should().OnlyContain(s => s.IsActive);
    }

    [Fact]
    public async Task DuplicateIdInPayload_DoesNotFailTheSync()
    {
        RiotReturns(Riot(5, "A"), Riot(5, "A"));

        var result = await Sync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Added.Should().Be(1);
    }

    [Fact]
    public async Task UnchangedStations_AreNotCountedAsUpdated()
    {
        Seed("A", "1");
        RiotReturns(Riot(1, "A"));

        var result = await Sync();

        result.Value.Updated.Should().Be(0);
    }

    [Fact]
    public async Task MassRemoval_IsHeld_OtherChangesStillApplied()
    {
        for (var i = 1; i <= 10; i++) Seed($"S{i}", i.ToString());
        // 3 of 10 gone (30% > 20%), one renamed, one new.
        RiotReturns(Enumerable.Range(1, 7).Select(i => Riot(i, i == 1 ? "S1_RENAMED" : $"S{i}"))
            .Append(Riot(99, "NEW")).ToArray());

        var result = await Sync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Deactivated.Should().Be(0);
        result.Value.HeldStations.Should().BeEquivalentTo("S10", "S8", "S9");
        result.Value.Added.Should().Be(1);
        result.Value.Updated.Should().Be(1);
        Rows().Should().OnlyContain(s => s.IsActive);
    }

    [Fact]
    public async Task MassRemoval_Confirmed_Deactivates()
    {
        for (var i = 1; i <= 10; i++) Seed($"S{i}", i.ToString());
        RiotReturns(Enumerable.Range(1, 7).Select(i => Riot(i, $"S{i}")).ToArray());

        var result = await Sync(confirm: true);

        result.Value.Deactivated.Should().Be(3);
        result.Value.HeldStations.Should().BeNull();
    }

    [Fact]
    public async Task RemovalWithinLimit_IsNotHeld()
    {
        for (var i = 1; i <= 10; i++) Seed($"S{i}", i.ToString());
        RiotReturns(Enumerable.Range(1, 8).Select(i => Riot(i, $"S{i}")).ToArray());

        var result = await Sync();

        result.Value.Deactivated.Should().Be(2);
        result.Value.HeldStations.Should().BeNull();
    }

    [Fact]
    public async Task RemovedStation_IsDeactivated_AndItsLookupCacheDropped()
    {
        Seed("A", "1");
        var gone = Seed("B", "2");
        Seed("C", "3");
        Seed("D", "4");
        Seed("E", "5");
        RiotReturns(Riot(1, "A"), Riot(3, "C"), Riot(4, "D"), Riot(5, "E"));

        var result = await Sync();

        result.Value.Deactivated.Should().Be(1);
        Rows().Single(s => s.Id == gone.Id).IsActive.Should().BeFalse();
        await _cache.Received().RemoveAsync($"station:lookup:{gone.Id}", Arg.Any<CancellationToken>());
        await _cache.Received().RemoveAsync("station:lookup:B", Arg.Any<CancellationToken>());
    }
}

public class PurgeStationsHandlerTests
{
    private readonly FacilityDbContext _db = new(new DbContextOptionsBuilder<FacilityDbContext>()
        .UseInMemoryDatabase("purge-" + Guid.NewGuid()).Options);
    private readonly IRiot3FacilityClient _riot3 = Substitute.For<IRiot3FacilityClient>();
    private readonly IStationUsageReader _usage = Substitute.For<IStationUsageReader>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly Map _map;

    public PurgeStationsHandlerTests()
    {
        _map = new Map(Guid.NewGuid(), "FAN1_MAP", "v1", 100, 100, "{}");
        _map.SetVendorRef("2");
        _db.Maps.Add(_map);
        _db.SaveChanges();
        _usage.FindInUseAsync(Arg.Any<IReadOnlyCollection<StationUsageCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        RiotReturns(new Riot3StationInfo(1, "LIVE", 0, 0, 0));
    }

    private Station Seed(string name, string? vendorRef, bool active, string? code = null)
    {
        var s = new Station(Guid.NewGuid(), _map.Id, name, new Coordinate(0, 0), StationType.Normal);
        if (vendorRef is not null) s.SetVendorRef(vendorRef);
        s.SetCode(code ?? name);
        if (!active) s.Deactivate();
        _db.Stations.Add(s);
        _db.SaveChanges();
        return s;
    }

    private void RiotReturns(params Riot3StationInfo[] stations)
        => _riot3.GetStationsAsync(2, Arg.Any<CancellationToken>()).Returns(stations.ToList());

    private Task<DTMS.SharedKernel.Messaging.Result<PurgeStationsResult>> Purge(IReadOnlyList<Guid>? ids = null)
        => new PurgeStationsCommandHandler(
                new MapRepository(_db), new StationRepository(_db), _riot3, _usage, _cache,
                NullLogger<PurgeStationsCommandHandler>.Instance)
            .Handle(new PurgeStationsCommand(_map.Id, ids), CancellationToken.None);

    private List<Station> Rows() => _db.Stations.AsNoTracking().ToList();

    [Fact]
    public async Task PurgeAll_DeletesOnlyInactiveRiotStationsThatAreGoneAndUnused()
    {
        var live = Seed("LIVE", "1", active: true);
        var gone = Seed("ATH_01", "119", active: false);
        var used = Seed("F_21", "189", active: false);
        var manual = Seed("HAND", null, active: true);
        _usage.FindInUseAsync(Arg.Any<IReadOnlyCollection<StationUsageCandidate>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [used.Id] = "active order template 'X' routes through it" });

        var result = await Purge();

        result.IsSuccess.Should().BeTrue();
        result.Value.Purged.Should().ContainSingle(p => p.Id == gone.Id);
        result.Value.Skipped.Should().ContainSingle(s => s.Id == used.Id && s.Reason.Contains("template"));
        Rows().Select(s => s.Id).Should().BeEquivalentTo(new[] { live.Id, used.Id, manual.Id });
        await _cache.Received().RemoveAsync("station:lookup:ATH_01", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsageCheck_GetsRiotMapAndStationIds_ForTemplateMoveMissions()
    {
        var gone = Seed("ATH_01", "119", active: false);
        IReadOnlyCollection<StationUsageCandidate>? asked = null;
        _usage.FindInUseAsync(Arg.Do<IReadOnlyCollection<StationUsageCandidate>>(c => asked = c), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [gone.Id] = "active order template 'X' MOVEs to it" });

        var result = await Purge();

        asked.Should().BeEquivalentTo(new[] { new StationUsageCandidate(gone.Id, 2, 119) });
        result.Value.Purged.Should().BeEmpty();
        result.Value.Skipped.Should().ContainSingle(s => s.Id == gone.Id && s.Reason.Contains("MOVEs"));
        Rows().Should().Contain(s => s.Id == gone.Id);
    }

    [Fact]
    public async Task StationRiotStillLists_IsSkipped()
    {
        // Inactive in DTMS (sync hasn't run yet) but RIOT3 has it again.
        var back = Seed("STF_9", "9", active: false);
        RiotReturns(new Riot3StationInfo(9, "STF_9", 0, 0, 0));

        var result = await Purge();

        result.Value.Purged.Should().BeEmpty();
        result.Value.Skipped.Should().ContainSingle(s => s.Id == back.Id && s.Reason.Contains("still has it"));
    }

    [Fact]
    public async Task StationRecreatedUnderNewIdWithSameName_IsSkippedForTheSyncToTakeOver()
    {
        var old = Seed("STF_21", "125", active: false);
        RiotReturns(new Riot3StationInfo(300, "STF_21", 0, 0, 0));

        var result = await Purge();

        result.Value.Purged.Should().BeEmpty();
        result.Value.Skipped.Should().ContainSingle(s => s.Id == old.Id && s.Reason.Contains("same name"));
    }

    [Fact]
    public async Task ExplicitIds_ActiveOrManualOrForeign_AreSkippedWithReason()
    {
        var active = Seed("LIVE", "1", active: true);
        var manual = Seed("HAND", null, active: false);
        var gone = Seed("ATH_01", "119", active: false);

        var result = await Purge([active.Id, manual.Id, gone.Id, Guid.NewGuid()]);

        result.Value.Purged.Should().ContainSingle(p => p.Id == gone.Id);
        result.Value.Skipped.Should().HaveCount(3);
        Rows().Should().HaveCount(2);
    }

    [Fact]
    public async Task RiotUnreachable_DeletesNothing()
    {
        Seed("ATH_01", "119", active: false);
        _riot3.GetStationsAsync(2, Arg.Any<CancellationToken>())
            .Returns<List<Riot3StationInfo>>(_ => throw new HttpRequestException("down"));

        var result = await Purge();

        result.IsFailure.Should().BeTrue();
        Rows().Should().HaveCount(1);
    }
}

public class Riot3FacilityClientTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private static Riot3FacilityClient Client(string body)
        => new(new HttpClient(new StubHandler(body)) { BaseAddress = new Uri("http://riot3") });

    [Fact]
    public async Task GetStations_ErrorEnvelopeWithHttp200_Throws()
    {
        // Verbatim RIOT3 answer for an unknown map id (2026-10-27).
        var act = () => Client("""{"code":"E800075","message":"地图不存在"}""").GetStationsAsync(999999);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*E800075*");
    }

    [Fact]
    public async Task GetStations_SuccessWithEmptyList_ReturnsEmpty()
    {
        var stations = await Client("""{"code":"0","data":[]}""").GetStationsAsync(2);

        stations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMap_ErrorEnvelope_Throws()
    {
        var act = () => Client("""{"code":"E1","message":"boom"}""").GetMapAsync(2);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
