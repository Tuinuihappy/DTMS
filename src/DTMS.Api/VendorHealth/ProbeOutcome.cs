namespace AMR.DeliveryPlanning.Api.VendorHealth;

public enum ProbeOutcomeKind
{
    Success,
    Auth,
    Failure
}

public sealed record ProbeOutcome(
    ProbeOutcomeKind Kind,
    string? Code,
    string? Message,
    int LatencyMs,
    string? FailureReason);
