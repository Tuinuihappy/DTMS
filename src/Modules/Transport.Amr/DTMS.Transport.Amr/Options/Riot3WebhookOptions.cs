namespace DTMS.Transport.Amr.Options;

/// <summary>
/// Auth gates for the RIOT3 webhook endpoint. RIOT3 v4 has no built-in
/// signature/header authentication (verified against the Delta6FAN1
/// admin UI), so we layer two vendor-agnostic defences:
///
///   1. IP allowlist  — only accept requests whose remote address is in
///      <see cref="AllowedIPs"/>. Skipped when the list is empty.
///   2. URL-path secret — DTMS tells RIOT3 the notification URL is
///      "/api/webhooks/riot3/notify/{secret}". Requests missing the
///      segment, or sending a value that matches none of
///      <see cref="UrlSecrets"/>, are rejected. Skipped when the list
///      is empty.
///
/// <see cref="UrlSecrets"/> is an array so secrets can be rotated
/// without downtime: add the new value to the array, update RIOT3
/// admin URL, wait until traffic has migrated, then drop the old
/// value. Webhook callbacks stay accepted at every step.
///
/// <see cref="RequireAuth"/> gates the rollout: false logs warnings but
/// still processes the request; true enforces both checks and returns
/// 403 (IP) or 404 (path mismatch).
/// </summary>
public class Riot3WebhookOptions
{
    public const string SectionName = "VendorAdapter:Riot3:Webhook";

    /// <summary>
    /// Off by default — staged rollout flag. Flip to true once RIOT3 has
    /// been pointed at the secret URL and the source IP is confirmed in
    /// the warn-mode logs.
    /// </summary>
    public bool RequireAuth { get; set; } = false;

    /// <summary>
    /// Allowed RIOT3 source IPs. Bind via indexed env vars
    /// (e.g. VendorAdapter__Riot3__Webhook__AllowedIPs__0=10.204.212.28).
    /// Empty array disables the IP check.
    /// </summary>
    public string[] AllowedIPs { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Accepted URL-path secrets. A request matches when its {secret}
    /// route segment equals any entry. Use multiple entries to perform
    /// zero-downtime secret rotation:
    ///   UrlSecrets__0=&lt;current&gt;
    ///   UrlSecrets__1=&lt;next&gt;     ← added during rotation window
    /// Drop the old value once RIOT3 has migrated to the new URL.
    /// Empty array disables the path check.
    /// </summary>
    public string[] UrlSecrets { get; set; } = Array.Empty<string>();
}
