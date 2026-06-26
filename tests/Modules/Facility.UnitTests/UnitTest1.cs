using DTMS.Facility.Domain.Entities;
using DTMS.Facility.Domain.ValueObjects;
using FluentAssertions;

namespace Facility.UnitTests;

public class StationActionTests
{
    private static Station NewStation()
        => new(Guid.NewGuid(), Guid.NewGuid(), "S1", new Coordinate(0, 0), StationType.Normal);

    [Fact]
    public void NewStation_HasNoActions()
    {
        var station = NewStation();

        station.Actions.Should().BeNull("a freshly created station defaults to a pure MOVE waypoint");
    }

    [Fact]
    public void SetActions_StoresEntriesKeyedByIntent()
    {
        var station = NewStation();
        var lift = new StationAction("standardRobotsCustom", "agv", new Dictionary<string, string>
        {
            ["id"] = "4", ["param0"] = "1", ["param1"] = "0"
        });
        var drop = new StationAction("standardRobotsCustom", "agv", new Dictionary<string, string>
        {
            ["id"] = "4", ["param0"] = "2", ["param1"] = "0"
        });

        station.SetActions(new Dictionary<string, StationAction>
        {
            ["lift"] = lift,
            ["drop"] = drop
        });

        station.Actions.Should().NotBeNull();
        var actions = station.Actions!;
        actions.Should().HaveCount(2);
        actions["lift"].Parameters!["param0"].Should().Be("1");
        actions["drop"].Parameters!["param0"].Should().Be("2");
    }

    [Fact]
    public void SetActions_KeysAreCaseInsensitive()
    {
        // Dispatch will look up by task type which it lower-cases ("lift"),
        // but operators may configure with mixed casing in the UI. The map
        // must collapse those to the same intent.
        var station = NewStation();
        station.SetActions(new Dictionary<string, StationAction>
        {
            ["LIFT"] = new("standardRobotsCustom")
        });

        station.Actions!.ContainsKey("lift").Should().BeTrue();
        station.Actions.ContainsKey("Lift").Should().BeTrue();
    }

    [Fact]
    public void SetActions_WithNull_ClearsTheMap()
    {
        var station = NewStation();
        station.SetActions(new Dictionary<string, StationAction>
        {
            ["lift"] = new("standardRobotsCustom")
        });

        station.SetActions(null);

        station.Actions.Should().BeNull();
    }

    [Fact]
    public void SetActions_WithEmpty_ClearsTheMap()
    {
        var station = NewStation();
        station.SetActions(new Dictionary<string, StationAction>
        {
            ["lift"] = new("standardRobotsCustom")
        });

        station.SetActions(new Dictionary<string, StationAction>());

        station.Actions.Should().BeNull("empty map collapses to null so callers can rely on null = no actions");
    }

    [Fact]
    public void SetActions_DefensivelyCopiesTheInput()
    {
        // Future-Phase-2 code may keep references to the dictionary passed
        // into SetActions and mutate it after the fact. The entity must
        // hold its own snapshot.
        var station = NewStation();
        var input = new Dictionary<string, StationAction>
        {
            ["lift"] = new("standardRobotsCustom")
        };
        station.SetActions(input);

        input.Clear();

        station.Actions!.Should().HaveCount(1);
    }

    [Fact]
    public void StationAction_DefaultsCategoryToAgv()
    {
        // RIOT3 callers can omit category for the common AMR case.
        var action = new StationAction("lift");

        action.Category.Should().Be("agv");
    }

    [Fact]
    public void StationAction_RejectsEmptyActionType()
    {
        var act = () => new StationAction("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StationAction_ValueEquality_ConsidersOrderedParameters()
    {
        // Equal parameter maps with the same keys/values in different
        // insertion order must hash and compare equal — Dispatch may
        // diff configs to detect changes during a sync.
        var a = new StationAction("X", "agv", new Dictionary<string, string>
        {
            ["a"] = "1", ["b"] = "2"
        });
        var b = new StationAction("X", "agv", new Dictionary<string, string>
        {
            ["b"] = "2", ["a"] = "1"
        });

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
