namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Bound to the <c>CallbackTokenRefresh</c> config section. Drives the
/// outbound-token auto-refresh background loop and the SSRF allowlist that both
/// the loop and the manual endpoint enforce.
/// </summary>
public sealed class CallbackTokenRefreshOptions
{
    public const string SectionName = "CallbackTokenRefresh";

    // No global Enabled flag: the background loop runs wherever the process-role
    // gate (Workers:CallbackTokenRefresh:RunInThisProcess) registered it, and
    // each system's own TokenRefreshConfig.enabled (the UI checkbox) is the
    // on/off control. This keeps enable/disable in the UI, not in env.

    /// <summary>Seconds between refresh sweeps. Must be comfortably smaller than
    /// each system's <c>refreshBeforeSeconds</c> or a token could lapse between
    /// ticks — validated at startup.</summary>
    public int PollIntervalSeconds { get; set; } = 3600;

    /// <summary>Delay before the first sweep so the app finishes warming up.</summary>
    public int StartupDelaySeconds { get; set; } = 30;

    /// <summary>Max systems minted concurrently per sweep — bounds load on the
    /// mint endpoints (thundering-herd guard).</summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>Per-system mint timeout. Also caps the HttpClient hard ceiling.</summary>
    public int MintTimeoutSeconds { get; set; } = 30;

    /// <summary>Hosts a mint <c>tokenUrl</c> is allowed to target (SSRF guard).
    /// Empty = deny all — an operator must opt each mint host in explicitly.
    /// Compared case-insensitively against the URL host (no port).</summary>
    public List<string> AllowedMintHosts { get; set; } = new();
}
