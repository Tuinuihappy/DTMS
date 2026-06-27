namespace DTMS.Api.VendorHealth;

public sealed record VendorHealthDto(
    string Vendor,
    string Status,
    string? Code,
    string? Message,
    string? FailureReason,
    int? LatencyMs,
    DateTime LastChangedAt,
    DateTime LastCheckedAt,
    int ConsecutiveSuccesses,
    int ConsecutiveFailures)
{
    public static VendorHealthDto From(VendorHealthSnapshot snapshot) => new(
        Vendor: snapshot.Vendor,
        Status: snapshot.Status.ToString(),
        Code: snapshot.LastOutcome?.Code,
        Message: snapshot.LastOutcome?.Message,
        FailureReason: snapshot.LastOutcome?.FailureReason,
        LatencyMs: snapshot.LastOutcome?.LatencyMs,
        LastChangedAt: snapshot.LastChangedAt,
        LastCheckedAt: snapshot.LastCheckedAt,
        ConsecutiveSuccesses: snapshot.ConsecutiveSuccesses,
        ConsecutiveFailures: snapshot.ConsecutiveFailures);
}
