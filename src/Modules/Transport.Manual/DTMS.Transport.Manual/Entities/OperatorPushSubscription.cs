using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;

// Phase 4.1 — Push notification target (per ADR-013).
// Single table polymorphic by Platform — WebPush stores VAPID p256dh + auth
// in PublicKey/AuthSecret; future FCM/APNS would use Endpoint as the
// device token and leave keys null. DeviceLabel is the user-facing hint
// ("Chrome on iPhone 15") so an operator with multiple devices can revoke
// the right one from the settings page.
public class OperatorPushSubscription
{
    public Guid Id { get; private set; }
    public Guid OperatorId { get; private set; }
    public PushPlatform Platform { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string? PublicKey { get; private set; }
    public string? AuthSecret { get; private set; }
    public string? DeviceLabel { get; private set; }
    public DateTime SubscribedAt { get; private set; }
    public DateTime? LastSucceededAt { get; private set; }
    public DateTime? LastFailedAt { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    private OperatorPushSubscription() { }

    internal static OperatorPushSubscription Create(
        Guid operatorId,
        PushPlatform platform,
        string endpoint,
        string? publicKey,
        string? authSecret,
        string? deviceLabel)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("OperatorId must not be empty.", nameof(operatorId));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint must not be empty.", nameof(endpoint));
        // WebPush requires both p256dh + auth secret per RFC 8291;
        // FCM/APNS use the endpoint as the device token and skip keys.
        if (platform == PushPlatform.WebPush &&
            (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(authSecret)))
            throw new ArgumentException(
                "Web Push subscriptions require PublicKey + AuthSecret.", nameof(publicKey));

        return new OperatorPushSubscription
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Platform = platform,
            Endpoint = endpoint,
            PublicKey = publicKey,
            AuthSecret = authSecret,
            DeviceLabel = deviceLabel,
            SubscribedAt = DateTime.UtcNow,
        };
    }

    internal void UpdateKeys(string? publicKey, string? authSecret, string? deviceLabel)
    {
        if (!string.IsNullOrWhiteSpace(publicKey)) PublicKey = publicKey;
        if (!string.IsNullOrWhiteSpace(authSecret)) AuthSecret = authSecret;
        if (!string.IsNullOrWhiteSpace(deviceLabel)) DeviceLabel = deviceLabel;
    }

    public void MarkDeliverySucceeded()
    {
        LastSucceededAt = DateTime.UtcNow;
        ConsecutiveFailures = 0;
    }

    public void MarkDeliveryFailed()
    {
        LastFailedAt = DateTime.UtcNow;
        ConsecutiveFailures++;
    }

    // 410 Gone from the push service → endpoint is dead; gateway should
    // call OperatorPushSubscription.RemovePushSubscription instead of just
    // marking failure. This flag lets the WebPushGateway decide.
    public bool ShouldEvict => ConsecutiveFailures >= 5;
}
