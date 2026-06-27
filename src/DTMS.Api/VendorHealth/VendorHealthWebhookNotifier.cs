using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Api.VendorHealth;

/// <summary>
/// Subscribes to <see cref="IVendorHealthStore.StatusChanged"/> and POSTs
/// a Slack-compatible message to a configured webhook URL whenever a
/// vendor crosses the severity threshold. The state machine already
/// debounces flap (3-fail / 2-recover for vendors, instant for Degraded
/// from upstream IHealthCheck returning Fx.Degraded), so every event we
/// receive here is a real incident — no extra throttling needed.
///
/// Disabled when <see cref="VendorHealthWebhookOptions.Url"/> is empty —
/// the notifier still registers + subscribes so toggling the URL in
/// config doesn't require a restart, but BroadcastAsync short-circuits.
///
/// Webhook failures are logged but never thrown — alerting infrastructure
/// must not crash the app.
/// </summary>
public sealed class VendorHealthWebhookNotifier : IHostedService
{
    private readonly IVendorHealthStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<VendorHealthWebhookOptions> _options;
    private readonly ILogger<VendorHealthWebhookNotifier> _logger;

    public VendorHealthWebhookNotifier(
        IVendorHealthStore store,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<VendorHealthWebhookOptions> options,
        ILogger<VendorHealthWebhookNotifier> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _store.StatusChanged += OnStatusChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _store.StatusChanged -= OnStatusChanged;
        return Task.CompletedTask;
    }

    private void OnStatusChanged(object? sender, VendorHealthSnapshot snapshot)
    {
        _ = NotifyAsync(snapshot);
    }

    private async Task NotifyAsync(VendorHealthSnapshot snapshot)
    {
        var opts = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opts.Url)) return;
        if (!ShouldNotify(snapshot.Status, opts)) return;

        try
        {
            using var http = _httpClientFactory.CreateClient(nameof(VendorHealthWebhookNotifier));
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));

            var payload = BuildPayload(snapshot, opts);
            using var response = await http.PostAsJsonAsync(opts.Url, payload);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Vendor health webhook returned {Status} for {Vendor} → {NewStatus}",
                    (int)response.StatusCode, snapshot.Vendor, snapshot.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Vendor health webhook failed for {Vendor} → {NewStatus}",
                snapshot.Vendor, snapshot.Status);
        }
    }

    private static bool ShouldNotify(VendorHealthStatus status, VendorHealthWebhookOptions opts)
    {
        if (status == VendorHealthStatus.Healthy)
            return opts.NotifyOnRecovery;

        return SeverityRank(status) >= SeverityRank(ParseSeverity(opts.MinSeverity));
    }

    private static VendorHealthStatus ParseSeverity(string raw) =>
        Enum.TryParse<VendorHealthStatus>(raw, ignoreCase: true, out var s)
            ? s
            : VendorHealthStatus.Degraded;

    private static int SeverityRank(VendorHealthStatus status) => status switch
    {
        VendorHealthStatus.Healthy => 0,
        VendorHealthStatus.Unknown => 1,
        VendorHealthStatus.Degraded => 2,
        VendorHealthStatus.Unhealthy => 3,
        _ => 0,
    };

    private static object BuildPayload(VendorHealthSnapshot snapshot, VendorHealthWebhookOptions opts)
    {
        var icon = snapshot.Status switch
        {
            VendorHealthStatus.Healthy => ":large_green_circle:",
            VendorHealthStatus.Degraded => ":large_yellow_circle:",
            VendorHealthStatus.Unhealthy => ":red_circle:",
            _ => ":white_circle:",
        };

        var reason = snapshot.LastOutcome?.FailureReason
            ?? snapshot.LastOutcome?.Message
            ?? "(no detail)";

        var text = $"{icon} *[{opts.EnvironmentLabel}]* `{snapshot.Vendor}` is now *{snapshot.Status}*"
                   + (snapshot.Status == VendorHealthStatus.Healthy
                       ? $" (recovered after {snapshot.ConsecutiveFailures} prior failures)"
                       : $"\n> {reason}");

        // Slack incoming-webhook minimum format. Discord + MS Teams also
        // accept this shape (Discord requires `username` not to clash;
        // Teams formats markdown slightly differently). For richer
        // formatting, swap the URL to point at a webhook proxy that
        // transforms `text` → vendor-specific payload.
        return new { text };
    }
}
