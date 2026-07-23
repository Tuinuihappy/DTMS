using DTMS.Dispatch.Domain.Repositories;
using FluentAssertions;

namespace Dispatch.UnitTests;

// RC3 duplicate-vs-retry rules. The two guards under test each close a
// real "phantom retry" hole found in plan review:
//   Fix A — compare against EVERY stored attempt (delayed re-delivery of an
//           old frame must match its own row, not be measured against the
//           latest attempt only).
//   Fix B — never mint an occurrence on a mission that has not FAILED
//           (RIOT only retries after failure; far-off re-emissions on
//           healthy missions are clock artifacts or resume frames).
public class MissionRetryPolicyTests
{
    private static readonly DateTime T0 = new(2026, 7, 17, 8, 58, 47, DateTimeKind.Utc);

    // ── IsSameAttempt: the 5s window ────────────────────────────────────

    [Theory]
    [InlineData(0)]      // identical time — vendor re-delivery
    [InlineData(1)]      // webhook vs order-GET rounding skew (seen on RIOT order 5106)
    [InlineData(-1)]
    [InlineData(4.9)]    // just inside the window
    [InlineData(-4.9)]
    [InlineData(5)]      // boundary is inclusive
    public void IsSameAttempt_WithinWindow_True(double deltaSeconds)
        => MissionRetryPolicy.IsSameAttempt(T0, T0.AddSeconds(deltaSeconds)).Should().BeTrue();

    [Theory]
    [InlineData(5.1)]    // just outside
    [InlineData(38)]     // real retry gap observed on trip eed7f43a (E112045)
    [InlineData(-38)]
    [InlineData(600)]    // real retry gap observed on trip 5018 (E230001)
    public void IsSameAttempt_OutsideWindow_False(double deltaSeconds)
        => MissionRetryPolicy.IsSameAttempt(T0, T0.AddSeconds(deltaSeconds)).Should().BeFalse();

    // ── IsGenuineRetry: near-any × has-failed matrix ────────────────────

    [Fact]
    public void FarFromEveryAttempt_AndMissionFailed_IsRetry()
        => MissionRetryPolicy.IsGenuineRetry(nearAnyExistingAttempt: false, missionHasFailed: true)
            .Should().BeTrue();

    [Fact]
    public void NearAnyAttempt_IsDuplicate_EvenWhenFailed()
        // Fix A scenario: a delayed duplicate of attempt 1 arrives after
        // attempt 2 exists — it is near ITS OWN row, so nearAny is true and
        // no phantom attempt 3 is minted.
        => MissionRetryPolicy.IsGenuineRetry(nearAnyExistingAttempt: true, missionHasFailed: true)
            .Should().BeFalse();

    [Fact]
    public void NeverFailedMission_NeverMints_EvenWhenFar()
        // Fix B scenario: UtcNow-fallback timestamps or a resume-after-pause
        // re-emission drift far apart on a healthy mission — still not a retry.
        => MissionRetryPolicy.IsGenuineRetry(nearAnyExistingAttempt: false, missionHasFailed: false)
            .Should().BeFalse();

    [Fact]
    public void NearAndNeverFailed_IsDuplicate()
        => MissionRetryPolicy.IsGenuineRetry(nearAnyExistingAttempt: true, missionHasFailed: false)
            .Should().BeFalse();
}
