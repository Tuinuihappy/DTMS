namespace AMR.DeliveryPlanning.Transport.Manual.Infrastructure.Push;

// Bound from configuration section "Push:Vapid". Generated once via
// any web-push-libs CLI or `WebPushClient.GenerateVapidKeys()` — the
// keypair is per-deployment, not per-user. Public key is shipped to
// the PWA so the SW can include it in subscribe() options; private
// key signs outgoing pushes server-side.
//
// Subject is mailto: or https:// per RFC 8292 §2.4 — push services
// (FCM, Mozilla, etc.) include this in the JWT they verify, so put
// something a service operator can contact you at if your pushes
// behave badly.
public sealed class VapidOptions
{
    public const string SectionName = "Push:Vapid";

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = "mailto:ops@dtms.local";

    // When both keys are empty, Phase 4.3 runs in a "no-op gateway"
    // mode — the gateway logs the call but doesn't actually contact
    // push services. Lets the rest of the stack run before VAPID keys
    // are generated.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey);
}
