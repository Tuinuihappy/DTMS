namespace AMR.DeliveryPlanning.Api.VendorHealth;

public sealed class VendorHealthWebhookOptions
{
    /// <summary>
    /// Slack/Discord/MS Teams incoming-webhook URL. When empty the
    /// notifier registers but never sends — safe default for dev.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Minimum severity that triggers a notification. Default Degraded:
    /// Healthy → Degraded fires, Healthy → Healthy never fires (the
    /// state-machine event itself only fires on transition, so this is
    /// purely a severity filter).
    /// </summary>
    public string MinSeverity { get; set; } = "Degraded";

    /// <summary>
    /// Send a follow-up message when a component recovers to Healthy.
    /// Off by default to keep alert noise low.
    /// </summary>
    public bool NotifyOnRecovery { get; set; } = true;

    /// <summary>
    /// Optional label included in every message (e.g. "DTMS prod"). Lets
    /// one Slack channel receive alerts from multiple environments
    /// without confusion.
    /// </summary>
    public string EnvironmentLabel { get; set; } = "DTMS";

    public int TimeoutSeconds { get; set; } = 5;
}
