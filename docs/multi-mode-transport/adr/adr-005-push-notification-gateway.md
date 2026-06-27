# ADR-005: Push Notification Gateway for Operator Mobile App

- **Status**: ⚠️ **Superseded by [ADR-013 Push Notification — Web Push](adr-013-push-notification-web-push.md)** (2026-06-25)
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [Phase 4: Transport.Manual](../phases/phase-4-transport-manual.md), [Manual Operator API](../api/manual-operator-api.md)

> **Superseded notice (2026-06-25):** ADR-005's FCM decision assumed native iOS/Android mobile clients. [ADR-012](adr-012-operator-mobile-stack-pwa.md) changed the mobile stack to PWA, which uses Web Push (W3C standard) instead of FCM/APNs SDKs. The `IPushNotificationGateway` interface from ADR-005 is preserved in ADR-013; only the gateway implementation changes from FCM to Web Push. See [ADR-013](adr-013-push-notification-web-push.md) for current decision.

## Context

Phase 4 introduces Manual transport mode — dispatcher assigns trip → operator receives push notification on mobile app → operator acknowledges + executes delivery

Push notification เป็น **load-bearing infrastructure** สำหรับ Manual mode:
- ถ้า push fail → operator ไม่รู้ว่ามีงาน → SLA breach
- ถ้า push delay → operator delayed acknowledgement → reschedule cost
- Pause/Resume/Cancel จาก dispatcher ต้อง realtime → push เร็วกว่า polling

Requirements:
1. **iOS + Android support** (operator app cross-platform)
2. **Multiple devices per operator** (work phone + tablet + spare)
3. **Reliability**: > 99% delivery (with monitoring)
4. **Latency**: P95 < 5 seconds end-to-end
5. **Cost**: predictable pricing at scale (1000+ operators)
6. **Compliance**: data residency (Thailand operator data may not leave region)
7. **Local development**: must work without external SaaS

## Decision

Use **Firebase Cloud Messaging (FCM)** as production push gateway, behind a **mode-agnostic `IPushNotificationGateway` interface** with multiple implementations:

```csharp
public interface IPushNotificationGateway
{
    Task<DeliveryResult> SendAsync(Guid operatorId, INotification notification, CancellationToken ct);
    Task<DeliveryResult> SendBatchAsync(IEnumerable<Guid> operatorIds, INotification notification, CancellationToken ct);
}

public sealed record DeliveryResult(int Delivered, int Failed, IReadOnlyList<string> FailedDeviceTokens);
```

**Implementations:**
- `FcmPushNotificationGateway` — production (Firebase Cloud Messaging HTTP v1 API)
- `InMemoryPushNotificationGateway` — local dev + unit tests
- `LoggingPushNotificationGateway` — wraps any impl, logs all sends (always on)

**Register per environment:**
```csharp
services.AddScoped<IPushNotificationGateway>(sp =>
{
    var inner = environment switch {
        "Development" => new InMemoryPushNotificationGateway(),
        _ => new FcmPushNotificationGateway(...)
    };
    return new LoggingPushNotificationGateway(inner, sp.GetRequiredService<ILogger<IPushNotificationGateway>>());
});
```

### Why FCM (vs alternatives)

1. **Cross-platform native**: ใช้ FCM ส่งทั้ง iOS (ผ่าน APNs proxy) + Android — 1 integration เดียว
2. **Free at our scale**: < 1M messages/day = ฟรี (Google bears infrastructure)
3. **Topic + condition messaging**: ส่งให้กลุ่ม operators ตาม warehouse ได้ (เช่น "all-bangkok-operators")
4. **Token management built-in**: FCM handles token rotation, invalid token cleanup
5. **Analytics + delivery reports**: built-in dashboard (BigQuery export available)
6. **Battle-tested**: ใช้ใน Uber, Lyft, food delivery apps ทั่วโลก

## Notification Schema

Define typed notifications (not free-form strings) — type-safe + versionable:

```csharp
namespace DTMS.Transport.Manual.Application.Notifications;

public interface INotification
{
    string Type { get; }
    string Title { get; }
    string Body { get; }
    IReadOnlyDictionary<string, string> Data { get; }
}

public sealed record TripAssignedNotification(Guid TripId, string WarehouseCode, DateTime ExpectedAckBy) : INotification
{
    public string Type => "TripAssigned";
    public string Title => "งานใหม่ได้รับมอบหมาย";
    public string Body => $"รับงานที่ {WarehouseCode} ภายใน {ExpectedAckBy:HH:mm}";
    public IReadOnlyDictionary<string, string> Data => new Dictionary<string, string>
    {
        ["tripId"] = TripId.ToString(),
        ["warehouseCode"] = WarehouseCode,
        ["expectedAckBy"] = ExpectedAckBy.ToString("o")
    };
}

// (TripPausedNotification, TripCancelledNotification, SlaReminderNotification, etc.)
```

Mobile app routes by `Type` field — adding new notification = new record + handler in app

## Alternatives Considered

### Alternative A: Apple Push Notification Service (APNs) + Direct Google FCM

ใช้ APNs สำหรับ iOS, FCM สำหรับ Android — 2 separate integrations

**Pros:**
- ไม่ต้องผ่าน Google (low-latency direct path สำหรับ iOS)
- ไม่ depend on Firebase account

**Cons:**
- 2 integrations to maintain
- 2 sets of credentials, error handling, retry logic
- APNs uses HTTP/2 stream — different lifecycle than HTTP

**Rejected because:** FCM v1 ส่งให้ APNs internally แล้ว — มี certificate management, retry, batching ให้แล้ว — เสีย complexity เปล่า

### Alternative B: Azure Notification Hubs

Microsoft's cross-platform push service

**Pros:**
- รัน Azure (matches potential infra direction)
- Tag-based routing (เหมือน FCM topic)
- Per-tenant isolation

**Cons:**
- Pricing: pay per push (vs FCM free tier)
- Smaller ecosystem documentation
- ใน Thailand: data residency ผ่าน Singapore region (vs FCM ผ่าน global Google)
- Team experience: ต่ำกว่า FCM

**Rejected because:** cost + ecosystem ไม่คุ้ม vs FCM free tier ที่ scale ที่เราคาดหวัง (1000 operators × 20 push/day = 20k/day → ฟรี)

### Alternative C: AWS SNS Mobile Push

**Pros:**
- ถ้าทีม AWS-native
- Single API for SMS + push + email

**Cons:**
- Pay per push
- ต้อง manage APNs + GCM credentials เอง (SNS ไม่ proxy ให้)
- Lock-in กับ AWS

**Rejected because:** team ไม่ AWS-native + pay per push

### Alternative D: Self-hosted WebSocket / SignalR for push

ใช้ existing SignalR infrastructure (มีอยู่แล้วใน DTMS) — push ผ่าน WebSocket แทน FCM

**Pros:**
- ไม่ depend on external service
- Already have SignalR hubs running
- Realtime latency < 1 second

**Cons:**
- WebSocket requires app foreground (iOS kills background sockets)
- Battery drain (always-on connection)
- ไม่ work เมื่อ app ถูกฆ่า — push notification ปลุก app ได้, WebSocket ไม่ได้
- Reinvent push delivery infrastructure (offline queueing, retry, ACK)

**Rejected because:** push notification fundamentally ต่างจาก WebSocket — push delivers when app not running ซึ่งเป็น critical requirement (operator มือว่าง อย่า assume app open)

**Note:** SignalR ยังใช้สำหรับ dispatcher console live update — แต่ไม่ใช่ operator push channel

### Alternative E: SMS as primary channel

**Pros:**
- ทำงานบนทุกเครื่อง (ไม่ต้อง app)
- Backup channel ถ้า push fail

**Cons:**
- Cost (per SMS)
- Latency (carrier-dependent)
- ไม่ rich data (text only)
- One-way (operator ตอบไม่ได้)

**Rejected as primary:** ใช้ SMS เป็น **fallback** เมื่อ push fail (planned for post-launch hardening)

## Implementation Details

### Service Account Setup (Production)

1. Create Firebase project: `dtms-production-th`
2. Enable FCM API
3. Generate service account JSON (Firebase Admin SDK)
4. Store credentials in Azure Key Vault / AWS Secrets Manager
5. Mount as env var: `FIREBASE_CREDENTIALS_JSON`

### Token Lifecycle

```csharp
// On login: register device + push token
POST /api/operator/auth/login
{ "deviceFingerprint": "...", "pushToken": "fcm-token-xxx" }
→ saves to transport_manual.operator_devices

// Token refresh (app side)
// Firebase auto-rotates tokens — app calls /api/operator/devices/refresh-token
POST /api/operator/devices/refresh-token
{ "deviceFingerprint": "...", "pushToken": "fcm-token-yyy" }
→ updates operator_devices.push_token

// On logout: invalidate
POST /api/operator/auth/logout
→ removes device from operator_devices
```

### Error Handling & Retries

```csharp
public async Task<DeliveryResult> SendAsync(Guid operatorId, INotification notification, CancellationToken ct)
{
    var devices = await _devices.GetActiveByOperatorIdAsync(operatorId, ct);
    if (devices.Count == 0)
        return new DeliveryResult(0, 0, Array.Empty<string>());  // no devices, no error

    var failed = new List<string>();
    var delivered = 0;

    foreach (var device in devices) {
        try {
            await _fcmClient.SendAsync(device.PushToken, notification, ct);
            delivered++;
        } catch (FcmInvalidTokenException) {
            // Token expired / app uninstalled — remove device
            await _devices.DeactivateAsync(device.Id, ct);
            failed.Add(device.PushToken);
        } catch (Exception ex) when (IsRetryable(ex)) {
            // Retry once with backoff
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            try { await _fcmClient.SendAsync(device.PushToken, notification, ct); delivered++; }
            catch { failed.Add(device.PushToken); }
        }
    }

    return new DeliveryResult(delivered, failed.Count, failed);
}
```

### Monitoring

- **Metric**: `dtms.push.sent` counter (tags: type, success/failure)
- **Metric**: `dtms.push.latency_ms` histogram (FCM API call duration)
- **Alert**: failure rate > 5% over 5 min → page on-call
- **Dashboard**: per-notification-type delivery rate + latency
- **Audit log**: every push call → `push_audit_log` table (kept 90 days)

### Local Development

```csharp
// InMemoryPushNotificationGateway — accessible via /api/dev/push-log
public sealed class InMemoryPushNotificationGateway : IPushNotificationGateway
{
    public ConcurrentBag<(Guid OperatorId, INotification Notification, DateTime SentAt)> Sent = new();

    public Task<DeliveryResult> SendAsync(Guid operatorId, INotification notification, CancellationToken ct)
    {
        Sent.Add((operatorId, notification, DateTime.UtcNow));
        return Task.FromResult(new DeliveryResult(1, 0, Array.Empty<string>()));
    }
}

// Dev endpoint to view sent pushes (for testing without real device)
// In Program.cs (Development only):
app.MapGet("/api/dev/push-log", (InMemoryPushNotificationGateway gw) => gw.Sent.ToArray());
```

## Consequences

### Positive

- ✓ Single integration covers iOS + Android
- ✓ Free at expected scale (< 1M messages/day)
- ✓ Token management + delivery reports handled by FCM
- ✓ Abstraction allows swapping gateway later (Azure NH if FCM ban, SMS fallback)
- ✓ InMemory impl enables hermetic local dev + tests

### Negative

- ✗ Depend on Google service availability (mitigated: monitoring + degraded mode)
- ✗ China deployment future = problem (Google services banned) → would need alternative gateway impl
- ✗ Notification payload limit: 4KB (FCM) — must keep payloads small
- ✗ iOS critical-alert push requires Apple entitlement (extra setup if we need critical-alert later)

### Neutral

- Web push (PWA notifications) supported by FCM — possible future channel
- Topic messaging available — can use for warehouse-wide announcements without per-operator iteration

## Migration / Rollout

### Phase 4 Implementation
1. Register Firebase project + service account
2. Add `firebase-admin` NuGet package (or use HTTP v1 API directly)
3. Implement `FcmPushNotificationGateway`
4. Implement `InMemoryPushNotificationGateway` (for tests)
5. Add `LoggingPushNotificationGateway` wrapper
6. Integration test: send to test device → verify receipt

### Post-Launch Hardening (out of scope)
- SMS fallback gateway (Twilio / Thai-Bulk-SMS)
- Critical-alert iOS entitlement
- Multi-region failover
- Per-tenant Firebase projects

## Acceptance Criteria

- [ ] `IPushNotificationGateway` interface defined in Transport.Manual.Application
- [ ] 3 implementations: Fcm, InMemory, Logging (wrapper)
- [ ] Unit tests: notification routing, fallback behavior, token invalidation
- [ ] Integration tests: full send → receive on test device
- [ ] Monitoring: metrics + alerts configured
- [ ] Documentation: per-notification-type table in [api/manual-operator-api.md](../api/manual-operator-api.md)

## References

- FCM HTTP v1 API: https://firebase.google.com/docs/cloud-messaging/send-message
- FCM Server Environments: https://firebase.google.com/docs/cloud-messaging/server
- APNs (for context): https://developer.apple.com/documentation/usernotifications
- Comparison study: https://www.airship.com/resources/explainer/push-notification-providers-comparison/
- [Manual Operator API — Push Payloads](../api/manual-operator-api.md#push-notification-payloads)
