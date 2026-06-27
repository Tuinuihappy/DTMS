namespace DTMS.Api.VendorHealth;

public static class VendorHealthStateMachine
{
    public static VendorHealthSnapshot Reduce(
        VendorHealthSnapshot? previous,
        ProbeOutcome outcome,
        Riot3HealthOptions options,
        DateTime now)
    {
        var prev = previous ?? VendorHealthSnapshot.Initial(previous?.Vendor ?? "riot3", now);

        var (successes, failures) = outcome.Kind switch
        {
            ProbeOutcomeKind.Success => (prev.ConsecutiveSuccesses + 1, 0),
            ProbeOutcomeKind.Auth => (0, 0),
            ProbeOutcomeKind.Failure => (0, prev.ConsecutiveFailures + 1),
            _ => (prev.ConsecutiveSuccesses, prev.ConsecutiveFailures)
        };

        var newStatus = DecideStatus(prev.Status, outcome.Kind, successes, failures, options);

        var changed = newStatus != prev.Status;
        return new VendorHealthSnapshot(
            Vendor: prev.Vendor,
            Status: newStatus,
            LastOutcome: outcome,
            LastChangedAt: changed ? now : prev.LastChangedAt,
            LastCheckedAt: now,
            ConsecutiveSuccesses: successes,
            ConsecutiveFailures: failures);
    }

    private static VendorHealthStatus DecideStatus(
        VendorHealthStatus current,
        ProbeOutcomeKind outcome,
        int successes,
        int failures,
        Riot3HealthOptions options)
    {
        if (outcome == ProbeOutcomeKind.Auth)
            return VendorHealthStatus.Degraded;

        switch (current)
        {
            case VendorHealthStatus.Unknown:
                if (outcome == ProbeOutcomeKind.Success) return VendorHealthStatus.Healthy;
                if (failures >= options.FailureThreshold) return VendorHealthStatus.Unhealthy;
                return VendorHealthStatus.Unknown;

            case VendorHealthStatus.Healthy:
                if (failures >= options.FailureThreshold) return VendorHealthStatus.Unhealthy;
                return VendorHealthStatus.Healthy;

            case VendorHealthStatus.Degraded:
                if (outcome == ProbeOutcomeKind.Success) return VendorHealthStatus.Healthy;
                if (failures >= options.FailureThreshold) return VendorHealthStatus.Unhealthy;
                return VendorHealthStatus.Degraded;

            case VendorHealthStatus.Unhealthy:
                if (outcome == ProbeOutcomeKind.Success && successes >= options.RecoveryThreshold)
                    return VendorHealthStatus.Healthy;
                return VendorHealthStatus.Unhealthy;

            default:
                return current;
        }
    }
}
