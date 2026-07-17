namespace DTMS.DeliveryOrder.Application.Consumers;

/// <summary>
/// Phase C (multi-source) — the audit EventType vocabulary for upstream
/// callback outcomes. System-NEUTRAL by design: which system a row concerns
/// lives in the <c>SystemKey</c> column, never in the label, so onboarding
/// sap/erp mints zero new event types and the frontend's stage sets stay
/// closed. (Historical <c>UpstreamOms*</c> rows were renamed to these by
/// migration <c>20260716150000</c>.)
///
/// <para>Consumed by <see cref="SourceCallbackOutcomeConsumer"/> (auto path),
/// the two resend handlers (manual path), their tests, and — string-for-string
/// — the frontend's <c>oms-notification-section</c> stage sets. Change a value
/// here and the frontend + a data migration must move with it.</para>
/// </summary>
public static class UpstreamCallbackAudit
{
    // shipment.started.v1 outcomes
    public const string Notified = "UpstreamNotified";
    public const string Rejected = "UpstreamRejected";
    public const string NotifyFailed = "UpstreamNotifyFailed";

    // shipment.arrived.v1 outcomes
    public const string ArrivedNotified = "UpstreamArrivedNotified";
    public const string ArrivedRejected = "UpstreamArrivedRejected";
    public const string ArrivedNotifyFailed = "UpstreamArrivedNotifyFailed";

    // shipment.cancelled.v1 outcomes
    public const string CancelledNotified = "UpstreamCancelledNotified";
    public const string CancelledRejected = "UpstreamCancelledRejected";
    public const string CancelledNotifyFailed = "UpstreamCancelledNotifyFailed";

    // operator-driven resends (sync path, written by the resend handlers)
    public const string ManuallyResent = "UpstreamManuallyResent";
    public const string ArrivedManuallyResent = "UpstreamArrivedManuallyResent";

    /// <summary>OrderActivity category for every row above.</summary>
    public const string Category = "UpstreamNotify";

    /// <summary>Projector name stamped on direct (non-projected) writes.</summary>
    public const string ProjectorName = "UpstreamNotifyDirect";
}
