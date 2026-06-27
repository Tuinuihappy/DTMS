namespace DTMS.Api.VendorHealth;

public sealed record VendorHealthSnapshot(
    string Vendor,
    VendorHealthStatus Status,
    ProbeOutcome? LastOutcome,
    DateTime LastChangedAt,
    DateTime LastCheckedAt,
    int ConsecutiveSuccesses,
    int ConsecutiveFailures)
{
    public static VendorHealthSnapshot Initial(string vendor, DateTime now) =>
        new(vendor, VendorHealthStatus.Unknown, LastOutcome: null,
            LastChangedAt: now, LastCheckedAt: now,
            ConsecutiveSuccesses: 0, ConsecutiveFailures: 0);
}
