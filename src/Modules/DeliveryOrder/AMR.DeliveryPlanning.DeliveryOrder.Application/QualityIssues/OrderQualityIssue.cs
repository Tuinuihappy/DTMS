namespace AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;

/// <summary>
/// Non-blocking quality finding on a delivery order. Surfaced in the response
/// of submit / confirm / upstream / bulk mutations and also persisted as an
/// <c>OrderAuditEvent</c> so the timeline shows when the warning was raised.
/// </summary>
public sealed record OrderQualityIssue(
    string Code,
    string Severity,
    string Field,
    string Message);

public static class QualityIssueCodes
{
    public const string ItemWeightMissing = "ITEM_WEIGHT_MISSING";
}

public static class QualityIssueSeverity
{
    public const string Warning = "warning";
    public const string Info = "info";
}
