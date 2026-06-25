# ADR-013: Push Notification — Web Push (VAPID) + Gateway Abstraction

- **Status**: Accepted
- **Date**: 2026-06-25
- **Deciders**: Solo dev (DTMS)
- **Supersedes**: [ADR-005 Push Notification Gateway (FCM)](adr-005-push-notification-gateway.md)
- **Related**: [ADR-012 PWA Mobile Stack](adr-012-operator-mobile-stack-pwa.md)

## Context

ADR-005 (2026-06-22) chose **FCM (Firebase Cloud Messaging)** as production push gateway, behind `IPushNotificationGateway` abstraction. ADR-005 assumed native iOS/Android mobile clients (per implicit assumption in 2026-06-22 ADR set).

**Context shift in ADR-012:** mobile stack = PWA, not native. PWA uses **Web Push API** (W3C standard) — browser handles delivery via its own push service (Chrome → FCM, Safari → APNs, Firefox → Mozilla). DTMS server sends VAPID-signed payloads; no Firebase SDK / APNs cert needed.

Phase 4 operator app needs push for:
- New job assignment notification
- Trip pause / resume / cancel alerts from dispatcher
- SLA deadline warnings
- POD reminder

## Decision

Use **Web Push (VAPID)** as MVP push gateway, behind `IPushNotificationGateway` abstraction (interface preserved from ADR-005).

### Stack
- **Library**: `WebPush` NuGet package (.NET) for VAPID signing
- **Keys**: VAPID key pair (public/private) generated once, stored in config
- **Subscription**: browser-generated Service Worker push subscription
- **Endpoint URL**: per-browser push service (Chrome / Firefox / Safari)

### Interface (preserved from ADR-005, supersedes implementation only)
```csharp
public interface IPushNotificationGateway
{
    Task<DeliveryResult> SendAsync(Guid operatorId, INotification notification, CancellationToken ct);
    Task<DeliveryResult> SendBatchAsync(IEnumerable<Guid> operatorIds, INotification notification, CancellationToken ct);
}
```

### Implementations
- `WebPushGateway` — production (Phase 4 MVP)
- `InMemoryPushNotificationGateway` — local dev + unit tests (preserved from ADR-005)
- `LoggingPushNotificationGateway` — wraps any impl, logs all sends (preserved)

### Subscription storage
Single table polymorphic by `Platform`:

```sql
CREATE TABLE transport_manual.OperatorPushSubscriptions (
    Id uuid PK,
    OperatorId uuid REFERENCES Operators(Id),
    Platform varchar(20) NOT NULL,        -- 'WebPush' | 'Fcm' | 'Apns' (future)
    Endpoint text NOT NULL,               -- WebPush URL or FCM/APNs token
    PublicKey text NULL,                  -- WebPush p256dh key
    AuthSecret text NULL,                 -- WebPush auth secret
    DeviceLabel varchar(100) NULL,        -- "John's iPhone"
    CreatedAt timestamp,
    LastUsedAt timestamp NULL,
    ExpiresAt timestamp NULL              -- WebPush subscriptions expire
);
```

When a future React Native migration ships (per ADR-012 future-proofing), add `FcmGateway` + `ApnsGateway` impls — same interface, same subscription table, filter by `Platform` column.

## Reasoning — Why Web Push over alternatives

### Alternatives considered

| Option | Verdict |
|---|---|
| **Web Push (VAPID)** ⭐ | Native to PWA — chosen |
| FCM direct + APNs direct | Would need native app — see ADR-005 (now superseded) |
| Third-party (OneSignal/Pusher) | Vendor lock-in, monthly cost, over-featured for our needs |
| Polling (no push) | Wastes battery, latency 30s+, doesn't scale |

### Web Push delivery rate vs FCM direct

| Aspect | Web Push | FCM direct (native) |
|---|---|---|
| Delivery rate | ~95% (browser-mediated) | ~99% (priority hints) |
| Latency P95 | 2-10s | 1-3s |
| Payload size | 4KB max | 4KB max (same) |
| Priority hints | None | Yes (high/normal) |
| Background restrictions | Browser policy | OS policy |
| iOS support | 16.4+ (Sep 2023) | All iOS |
| Setup complexity | Low (VAPID keys) | Medium (Firebase project + APNs cert) |

For our use case (job assignment, trip alerts) — 95% delivery + 5-10s latency is acceptable. Critical SLAs back up with polling fallback.

### Cost

- **Web Push**: $0 — uses public web push infrastructure (Mozilla/Google/Apple)
- **FCM direct**: $0 at our scale (< 1M messages/day), but requires Firebase project setup
- **APNs direct**: $99/yr Apple Developer membership (per ADR-005)

## Implementation Sketch

### Server-side notification send
```csharp
public sealed class WebPushGateway : IPushNotificationGateway
{
    private readonly WebPushClient _webPush;
    private readonly IOperatorSubscriptionRepository _subs;
    
    public async Task<DeliveryResult> SendAsync(
        Guid operatorId, INotification notification, CancellationToken ct)
    {
        var subscriptions = await _subs.GetForOperator(operatorId, ct);
        var payload = JsonSerializer.Serialize(new {
            type = notification.Type,
            title = notification.Title,
            body = notification.Body,
            data = notification.Data
        });
        
        int delivered = 0, failed = 0;
        var failedTokens = new List<string>();
        
        foreach (var sub in subscriptions.Where(s => s.Platform == "WebPush"))
        {
            try
            {
                var pushSub = new PushSubscription(
                    sub.Endpoint, sub.PublicKey, sub.AuthSecret);
                await _webPush.SendNotificationAsync(pushSub, payload);
                delivered++;
            }
            catch (WebPushException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                // Subscription expired — delete + flag for re-subscribe on next login
                await _subs.DeleteAsync(sub.Id, ct);
                failed++;
                failedTokens.Add(sub.Endpoint);
            }
        }
        
        return new DeliveryResult(delivered, failed, failedTokens);
    }
}
```

### PWA Service Worker (operator side)
```javascript
// public/sw.js
self.addEventListener('push', event => {
    const payload = event.data?.json() ?? {};
    event.waitUntil(
        self.registration.showNotification(payload.title, {
            body: payload.body,
            data: payload.data,
            tag: payload.type,
            icon: '/icons/operator-192.png',
            badge: '/icons/operator-badge.png'
        })
    );
});

self.addEventListener('notificationclick', event => {
    const tripId = event.notification.data?.tripId;
    if (tripId) {
        event.waitUntil(clients.openWindow(`/m/trips/${tripId}`));
    }
});
```

### PWA subscription registration (after login)
```typescript
async function registerForPush(jwtToken: string) {
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') return;
    
    const reg = await navigator.serviceWorker.ready;
    const subscription = await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: VAPID_PUBLIC_KEY,
    });
    
    await fetch('/api/operator/devices/register-push', {
        method: 'POST',
        headers: { Authorization: `Bearer ${jwtToken}` },
        body: JSON.stringify({
            endpoint: subscription.endpoint,
            keys: subscription.toJSON().keys,
            deviceLabel: navigator.userAgent,
        }),
    });
}
```

## Consequences

### Positive
- ✅ Zero infra cost (no Firebase, no APNs cert)
- ✅ 1 day setup (VAPID keys + library)
- ✅ Works on all PWA-capable browsers
- ✅ Interface preserves future-swap to FCM/APNs (per ADR-005)

### Negative
- ❌ Lower delivery SLA than FCM direct (95% vs 99%)
- ❌ No priority hints (OS decides delivery order)
- ❌ iOS push restricted to 16.4+ (mitigation: 95%+ adoption by 2026)
- ❌ Subscription expiration handling needed (less stable than FCM token)

### Migration trigger
If PWA → React Native migration happens (per ADR-012), add `FcmGateway` + `ApnsGateway` implementations:
- New rows in `OperatorPushSubscriptions` with `Platform = 'Fcm' | 'Apns'`
- `WebPushGateway` continues serving any remaining PWA users (gradual cutover)
- Send logic dispatches by `Platform` column → existing `IPushNotificationGateway` consumers don't change

## Why this supersedes ADR-005

ADR-005's FCM decision assumed native mobile (the implicit baseline of 2026-06-22 ADR set). ADR-012 changes that baseline to PWA. FCM **direct** integration doesn't apply to PWA — PWA uses browser's push service (which internally calls FCM for Chrome, but DTMS code never touches Firebase SDK).

The interface (`IPushNotificationGateway`) and registration patterns from ADR-005 remain valid — only the concrete gateway implementation changes from `FcmPushNotificationGateway` to `WebPushGateway`. Future native migration restores FCM as an additional implementation (not a replacement).
