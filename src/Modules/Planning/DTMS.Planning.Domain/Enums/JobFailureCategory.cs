namespace DTMS.Planning.Domain.Enums;

/// <summary>
/// Classification of why a Job entered <see cref="JobStatus.Failed"/> or
/// <see cref="JobStatus.Cancelled"/>. Companion to the free-text
/// <c>FailureReason</c> — the reason explains *what* happened to a human,
/// the category answers *which bucket* the failure belongs to without
/// pattern-matching on text (which is fragile across upstream message
/// changes).
///
/// <para>Set on every <c>MarkFailed</c>/<c>MarkCancelled</c> call from
/// Phase b13 onward; pre-b13 rows default to <see cref="None"/>. The
/// reason field is preserved as the human-readable detail.</para>
/// </summary>
public enum JobFailureCategory
{
    /// <summary>
    /// Not a failure — Job hasn't failed yet, OR failed before Phase b13
    /// shipped and was never categorized. Treat as "uncategorized" in
    /// reports.
    /// </summary>
    None = 0,

    /// <summary>OrderTemplate / DispatchOrderTemplateService couldn't find a template for the order shape.</summary>
    TemplateMissing = 1,

    /// <summary>Template was found but resolving its actions to concrete legs failed (e.g. station lookup miss).</summary>
    TemplateResolveFailed = 2,

    /// <summary>RIOT3 returned a 4xx or 5xx other than 429 — vendor refused the dispatch.</summary>
    VendorRejected = 3,

    /// <summary>RIOT3 returned 429 Too Many Requests — backoff applies, retry candidate.</summary>
    VendorRateLimited = 4,

    /// <summary>Vendor accepted the dispatch but later signalled failure via TripFailed webhook (Phase b9).</summary>
    VendorExecutionFailed = 5,

    /// <summary>Trip row never persisted (DB write fail / orphan) — the dispatcher couldn't link the Job to a Trip.</summary>
    TripPersistenceFailed = 6,

    /// <summary>Operator chose to cancel via TripCancelled webhook (Phase b9) — terminal, not retriable.</summary>
    OperatorCancelled = 7,

    /// <summary>
    /// Dispatcher threw an unexpected exception (network fault, DB connection lost,
    /// serialization error, …). Distinct from <see cref="VendorRejected"/> which
    /// is a business response from the vendor.
    /// </summary>
    DispatchException = 8,
}
