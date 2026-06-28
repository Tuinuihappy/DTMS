namespace DTMS.SharedKernel.Resilience;

/// <summary>
/// Cross-pod circuit breaker. State is held in shared storage (Redis)
/// so every replica sees the same open/closed verdict — when pod A trips
/// the breaker for system <c>oms</c>, pod B and C stop probing too.
/// Polly v8's in-process breaker does not solve this on its own because
/// it has no <c>StateProvider</c> input API; this interface is the
/// per-call gate that wraps Polly's local retry pipeline.
/// </summary>
public interface IDistributedCircuitBreaker
{
    /// <summary>
    /// Returns <c>true</c> if the caller may proceed (state is closed or
    /// half-open), <c>false</c> if the breaker is open. Atomic — the
    /// transition from open → half-open after the break window elapses
    /// happens inside the check so exactly one probe slips through.
    /// </summary>
    Task<bool> AllowAsync(string key, TimeSpan breakDuration, CancellationToken ct = default);

    /// <summary>
    /// Records the outcome of a call. Success closes the breaker and
    /// resets the failure count. Failure increments the counter and
    /// opens the breaker once <paramref name="failureThreshold"/> is
    /// reached; a failed probe in half-open state reopens immediately.
    /// </summary>
    Task RecordResultAsync(
        string key,
        bool success,
        int failureThreshold,
        TimeSpan breakDuration,
        CancellationToken ct = default);
}
