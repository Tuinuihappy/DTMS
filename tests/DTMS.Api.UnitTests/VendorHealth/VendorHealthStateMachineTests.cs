using DTMS.Api.VendorHealth;
using FluentAssertions;

namespace DTMS.Api.UnitTests.VendorHealth;

public class VendorHealthStateMachineTests
{
    private static readonly Riot3HealthOptions DefaultOptions = new()
    {
        FailureThreshold = 3,
        RecoveryThreshold = 2
    };

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TPrev = T0.AddMinutes(-5);

    private static ProbeOutcome Success(int latencyMs = 10) =>
        new(ProbeOutcomeKind.Success, Code: "0", Message: "SUCCESS",
            LatencyMs: latencyMs, FailureReason: null);

    private static ProbeOutcome Auth(int latencyMs = 10) =>
        new(ProbeOutcomeKind.Auth, Code: null, Message: null,
            LatencyMs: latencyMs,
            FailureReason: "RIOT3 reachable but ApiKey is invalid (401)");

    private static ProbeOutcome Failure(int latencyMs = 10, string reason = "RIOT3 returned HTTP 500") =>
        new(ProbeOutcomeKind.Failure, Code: null, Message: null,
            LatencyMs: latencyMs, FailureReason: reason);

    private static VendorHealthSnapshot SnapshotIn(
        VendorHealthStatus status,
        int consecutiveSuccesses = 0,
        int consecutiveFailures = 0) =>
        new("riot3", status, LastOutcome: null,
            LastChangedAt: TPrev, LastCheckedAt: TPrev,
            ConsecutiveSuccesses: consecutiveSuccesses,
            ConsecutiveFailures: consecutiveFailures);

    // ────────────────────────────────────────────────────────────────────
    // Group A — 16 transitions (table-driven, one per row in the design)
    // ────────────────────────────────────────────────────────────────────

    // — Unknown (1-4)

    [Fact]
    public void Unknown_PlusSuccess_GoesHealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unknown);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
    }

    [Fact]
    public void Unknown_PlusAuth_GoesDegraded()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unknown);

        var next = VendorHealthStateMachine.Reduce(prev, Auth(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Degraded);
    }

    [Fact]
    public void Unknown_PlusFailure_BelowThreshold_StaysUnknown()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unknown, consecutiveFailures: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unknown);
        next.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void Unknown_PlusFailure_AtThreshold_GoesUnhealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unknown, consecutiveFailures: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
        next.ConsecutiveFailures.Should().Be(3);
    }

    // — Healthy (5-8)

    [Fact]
    public void Healthy_PlusSuccess_StaysHealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 5);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
        next.ConsecutiveSuccesses.Should().Be(6);
    }

    [Fact]
    public void Healthy_PlusAuth_GoesDegraded()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 10);

        var next = VendorHealthStateMachine.Reduce(prev, Auth(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Degraded);
    }

    [Fact]
    public void Healthy_PlusFailure_BelowThreshold_StaysHealthy_Debounced()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveFailures: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
        next.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void Healthy_PlusFailure_AtThreshold_GoesUnhealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveFailures: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
        next.ConsecutiveFailures.Should().Be(3);
    }

    // — Degraded (9-12)

    [Fact]
    public void Degraded_PlusSuccess_GoesHealthy_AuthRecovered()
    {
        var prev = SnapshotIn(VendorHealthStatus.Degraded);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
        next.ConsecutiveSuccesses.Should().Be(1);
    }

    [Fact]
    public void Degraded_PlusAuth_StaysDegraded()
    {
        var prev = SnapshotIn(VendorHealthStatus.Degraded);

        var next = VendorHealthStateMachine.Reduce(prev, Auth(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Degraded);
    }

    [Fact]
    public void Degraded_PlusFailure_BelowThreshold_StaysDegraded()
    {
        var prev = SnapshotIn(VendorHealthStatus.Degraded, consecutiveFailures: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Degraded);
        next.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void Degraded_PlusFailure_AtThreshold_GoesUnhealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Degraded, consecutiveFailures: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
    }

    // — Unhealthy (13-16)

    [Fact]
    public void Unhealthy_PlusSuccess_BelowRecoveryThreshold_StaysUnhealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unhealthy, consecutiveSuccesses: 0);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
        next.ConsecutiveSuccesses.Should().Be(1);
    }

    [Fact]
    public void Unhealthy_PlusSuccess_AtRecoveryThreshold_GoesHealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unhealthy, consecutiveSuccesses: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
        next.ConsecutiveSuccesses.Should().Be(2);
    }

    [Fact]
    public void Unhealthy_PlusAuth_GoesDegraded()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unhealthy, consecutiveFailures: 10);

        var next = VendorHealthStateMachine.Reduce(prev, Auth(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Degraded);
    }

    [Fact]
    public void Unhealthy_PlusFailure_StaysUnhealthy()
    {
        var prev = SnapshotIn(VendorHealthStatus.Unhealthy, consecutiveFailures: 5);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
        next.ConsecutiveFailures.Should().Be(6);
    }

    // ────────────────────────────────────────────────────────────────────
    // Group B — Counter update rules
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Success_Increments_Successes_And_Resets_Failures()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 4, consecutiveFailures: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.ConsecutiveSuccesses.Should().Be(5);
        next.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Auth_Resets_Both_Counters()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 4, consecutiveFailures: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Auth(), DefaultOptions, T0);

        next.ConsecutiveSuccesses.Should().Be(0);
        next.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Failure_Increments_Failures_And_Resets_Successes()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 4, consecutiveFailures: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.ConsecutiveSuccesses.Should().Be(0);
        next.ConsecutiveFailures.Should().Be(2);
    }

    // ────────────────────────────────────────────────────────────────────
    // Group C — Timestamp rules
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LastCheckedAt_Always_Advances()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.LastCheckedAt.Should().Be(T0);
    }

    [Fact]
    public void LastChangedAt_Preserved_When_Status_Unchanged()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveSuccesses: 1);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.LastChangedAt.Should().Be(TPrev);
    }

    [Fact]
    public void LastChangedAt_Updated_When_Status_Transitions()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveFailures: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
        next.LastChangedAt.Should().Be(T0);
    }

    [Fact]
    public void LastOutcome_Is_Captured_On_Every_Reduce()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy);
        var outcome = Failure(latencyMs: 42, reason: "boom");

        var next = VendorHealthStateMachine.Reduce(prev, outcome, DefaultOptions, T0);

        next.LastOutcome.Should().BeSameAs(outcome);
    }

    // ────────────────────────────────────────────────────────────────────
    // Group D — Edge cases
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_Previous_Treated_As_Initial_Unknown()
    {
        var next = VendorHealthStateMachine.Reduce(previous: null, Success(), DefaultOptions, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
        next.Vendor.Should().Be("riot3");
        next.ConsecutiveSuccesses.Should().Be(1);
    }

    [Fact]
    public void Vendor_Name_Preserved_Across_Reduces()
    {
        var prev = SnapshotIn(VendorHealthStatus.Healthy) with { Vendor = "oms" };

        var next = VendorHealthStateMachine.Reduce(prev, Success(), DefaultOptions, T0);

        next.Vendor.Should().Be("oms");
    }

    [Fact]
    public void Custom_FailureThreshold_Is_Honored()
    {
        var options = new Riot3HealthOptions { FailureThreshold = 5, RecoveryThreshold = 2 };
        var prev = SnapshotIn(VendorHealthStatus.Healthy, consecutiveFailures: 3);

        var next = VendorHealthStateMachine.Reduce(prev, Failure(), options, T0);

        next.Status.Should().Be(VendorHealthStatus.Healthy);
        next.ConsecutiveFailures.Should().Be(4);
    }

    [Fact]
    public void Custom_RecoveryThreshold_Is_Honored()
    {
        var options = new Riot3HealthOptions { FailureThreshold = 3, RecoveryThreshold = 4 };
        var prev = SnapshotIn(VendorHealthStatus.Unhealthy, consecutiveSuccesses: 2);

        var next = VendorHealthStateMachine.Reduce(prev, Success(), options, T0);

        next.Status.Should().Be(VendorHealthStatus.Unhealthy);
        next.ConsecutiveSuccesses.Should().Be(3);
    }

    // ────────────────────────────────────────────────────────────────────
    // Group E — Sequential scenarios (the canonical traces from the plan)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Trace_From_Plan_HealthyTrips_To_Unhealthy_After_Three_Failures_Then_Recovers()
    {
        // t=0   Success → Unknown→Healthy
        // t=5   Success → Healthy
        // t=10  Failure → Healthy (debounced)
        // t=15  Success → Healthy (counter resets)
        // t=20  Failure → Healthy (debounced)
        // t=25  Failure → Healthy (debounced, F=2)
        // t=30  Failure → Healthy→Unhealthy (F=3)
        // t=35  Success → Unhealthy (still recovering, S=1)
        // t=40  Success → Unhealthy→Healthy (S=2, recovery threshold)

        VendorHealthSnapshot? s = null;
        var t = T0;

        s = VendorHealthStateMachine.Reduce(s, Success(), DefaultOptions, t);
        s.Status.Should().Be(VendorHealthStatus.Healthy);

        s = VendorHealthStateMachine.Reduce(s, Success(), DefaultOptions, t = t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Healthy);

        s = VendorHealthStateMachine.Reduce(s, Failure(), DefaultOptions, t = t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Healthy);
        s.ConsecutiveFailures.Should().Be(1);

        s = VendorHealthStateMachine.Reduce(s, Success(), DefaultOptions, t = t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Healthy);
        s.ConsecutiveFailures.Should().Be(0);

        s = VendorHealthStateMachine.Reduce(s, Failure(), DefaultOptions, t = t.AddSeconds(5));
        s = VendorHealthStateMachine.Reduce(s, Failure(), DefaultOptions, t = t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Healthy);
        s.ConsecutiveFailures.Should().Be(2);

        s = VendorHealthStateMachine.Reduce(s, Failure(), DefaultOptions, t = t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Unhealthy);

        s = VendorHealthStateMachine.Reduce(s, Success(), DefaultOptions, t = t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Unhealthy);
        s.ConsecutiveSuccesses.Should().Be(1);

        s = VendorHealthStateMachine.Reduce(s, Success(), DefaultOptions, t.AddSeconds(5));
        s.Status.Should().Be(VendorHealthStatus.Healthy);
        s.ConsecutiveSuccesses.Should().Be(2);
    }

    [Fact]
    public void Alternating_Fail_Success_Stays_Healthy_Forever()
    {
        // Anti-flap test: if a vendor blips once between successes, we must
        // never trip Unhealthy because the failure counter resets each success.
        VendorHealthSnapshot? s = VendorHealthStateMachine.Reduce(null, Success(), DefaultOptions, T0);

        for (var i = 0; i < 50; i++)
        {
            s = VendorHealthStateMachine.Reduce(s, Failure(), DefaultOptions, T0.AddSeconds(i * 2));
            s = VendorHealthStateMachine.Reduce(s, Success(), DefaultOptions, T0.AddSeconds(i * 2 + 1));
            s.Status.Should().Be(VendorHealthStatus.Healthy, $"iteration {i}");
        }
    }
}
