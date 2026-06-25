namespace AMR.DeliveryPlanning.Transport.Manual.Application.Services;

// Phase 4.3 — Push notification abstraction (per ADR-013). Today's
// implementation is Web Push (VAPID); future expansion to FCM/APNS
// switches the impl behind the same interface.
//
// Fan-out is the gateway's responsibility — caller passes the operator
// Id and the gateway loads every active OperatorPushSubscription for
// that operator, sends in parallel, and reports per-endpoint results
// so the caller can act on permanent failures (410 Gone → evict).
public interface IPushNotificationGateway
{
    // Sends a payload to every active subscription belonging to the
    // operator. Returns a per-endpoint outcome so the caller (typically
    // ManualDispatchStrategy in Phase 4.4) can update the
    // ConsecutiveFailures / ShouldEvict state on each subscription.
    Task<PushFanoutResult> SendToOperatorAsync(
        Guid operatorId,
        PushNotificationPayload payload,
        CancellationToken ct = default);
}

// Mirrors the Web Push notification shape — the PWA's SW reads these
// fields verbatim and renders into the OS notification tray.
public sealed record PushNotificationPayload(
    string Title,
    string Body,
    string? Url = null,        // SW navigates here on tap (default: /m/trips)
    string? Tag = null,        // browsers coalesce notifications with same tag
    string? Icon = null);      // overrides the SW default icon if set

public sealed record PushFanoutResult(
    int Sent,
    int Failed,
    IReadOnlyList<PushDeliveryOutcome> Outcomes);

public sealed record PushDeliveryOutcome(
    string Endpoint,
    bool Delivered,
    bool ShouldEvict,         // true on 404/410 → subscription is dead
    string? Error = null);
