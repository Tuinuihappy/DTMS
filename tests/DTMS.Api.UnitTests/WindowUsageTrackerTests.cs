using DTMS.Api.Infrastructure.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DTMS.Api.UnitTests;

/// <summary>A clock the test moves by hand. Small enough not to be worth a
/// dependency on Microsoft.Extensions.TimeProvider.Testing.</summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>
/// The tracker's counts are what the quotas get sized from, so they have to
/// mean the same thing the enforcing limiter means: requests in a sliding
/// window of the same shape, per caller.
/// </summary>
public class WindowUsageTrackerTests
{
    private static (WindowUsageTracker Tracker, TestClock Time) Sut(
        int userLimit = 10, int segments = 6)
    {
        var options = new RateLimitOptions
        {
            SegmentsPerWindow = segments,
            User = new ClassLimit { PermitLimit = userLimit, WindowSeconds = 60 },
        };
        var time = new TestClock(DateTimeOffset.Parse("2026-09-18T09:00:00Z"));
        return (new WindowUsageTracker(
            Options.Create(options),
            new RateLimitMetrics(time),
            NullLogger<WindowUsageTracker>.Instance,
            time), time);
    }

    private static RateLimitPartitionKey User(string id) => new(TrafficClass.User, $"user:{id}");

    [Fact]
    public void CountsRiseWithEachRequestFromTheSameCaller()
    {
        var (tracker, _) = Sut();

        tracker.Observe(User("a")).Should().Be(1);
        tracker.Observe(User("a")).Should().Be(2);
        tracker.Observe(User("a")).Should().Be(3);
    }

    [Fact]
    public void EachCallerIsCountedSeparately()
    {
        var (tracker, _) = Sut();

        tracker.Observe(User("a"));
        tracker.Observe(User("a"));

        tracker.Observe(User("b")).Should().Be(1, "one caller's traffic is not another's");
    }

    // The window slides: traffic older than a full window stops counting, one
    // segment at a time rather than all at once on a boundary.
    [Fact]
    public void TrafficLeavesTheWindowSegmentBySegment()
    {
        var (tracker, time) = Sut(segments: 6);

        tracker.Observe(User("a"));                       // segment 1
        time.Advance(TimeSpan.FromSeconds(10));
        tracker.Observe(User("a")).Should().Be(2);        // segment 2

        // 50s later the first segment has aged out, the second has not.
        time.Advance(TimeSpan.FromSeconds(50));
        tracker.Observe(User("a")).Should().Be(2);

        time.Advance(TimeSpan.FromSeconds(10));
        tracker.Observe(User("a")).Should().Be(2);
    }

    [Fact]
    public void AfterAFullQuietWindow_TheCallerStartsFromZeroAgain()
    {
        var (tracker, time) = Sut();

        tracker.Observe(User("a"));
        tracker.Observe(User("a"));
        time.Advance(TimeSpan.FromMinutes(2));

        tracker.Observe(User("a")).Should().Be(1);
    }

    [Fact]
    public void InfrastructureIsNotMeasured()
    {
        var (tracker, _) = Sut();

        tracker.Observe(new RateLimitPartitionKey(TrafficClass.Infra, string.Empty))
            .Should().BeNull();
    }

    // Webhooks are counted as requests for the anomaly signal, but they have no
    // quota, so there is nothing to compare a count against.
    [Fact]
    public void WebhooksAreCountedButHaveNoQuota()
    {
        var (tracker, _) = Sut();

        tracker.Observe(new RateLimitPartitionKey(TrafficClass.Webhook, string.Empty))
            .Should().BeNull();
    }

    [Fact]
    public void IdleCallersAreSweptAway()
    {
        var (tracker, time) = Sut();

        tracker.Observe(User("a"));
        tracker.TrackedCallers.Should().Be(1);

        time.Advance(TimeSpan.FromMinutes(5));
        tracker.Sweep();

        tracker.TrackedCallers.Should().Be(0);
    }

    [Fact]
    public void ActiveCallersSurviveASweep()
    {
        var (tracker, time) = Sut();

        tracker.Observe(User("a"));
        time.Advance(TimeSpan.FromSeconds(30));
        tracker.Sweep();

        tracker.TrackedCallers.Should().Be(1);
        tracker.Observe(User("a")).Should().Be(2, "the surviving count is still theirs");
    }

    // A clock that steps backwards must not resurrect expired traffic or throw.
    [Fact]
    public void AClockStepBackwardsKeepsCounting()
    {
        var (tracker, time) = Sut();

        tracker.Observe(User("a"));
        time.Advance(TimeSpan.FromSeconds(-30));

        tracker.Observe(User("a")).Should().Be(2);
    }
}
