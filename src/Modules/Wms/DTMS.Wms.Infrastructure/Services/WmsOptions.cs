namespace DTMS.Wms.Infrastructure.Services;

/// <summary>
/// Bound to the <c>Wms</c> configuration section. Token is treated as a
/// secret — resolve from user-secrets in dev, K8s/Key Vault in prod;
/// NEVER commit it to appsettings.json.
/// </summary>
public sealed class WmsOptions
{
    public const string SectionName = "Wms";

    /// <summary>
    /// Master switch for the WMS integration. When false: sync service
    /// skips its cycle, endpoint returns empty, downstream feature flag
    /// gating hides Manual/Fleet in the UI. Default false so a fresh
    /// deployment doesn't accidentally hammer an unconfigured endpoint.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Root URL of the WMS API — no trailing slash.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Rows per page requested from the WMS. Upstream default: 20.
    /// We pull 1000/page to minimize HTTP round-trips — the dataset today is
    /// ~156 rows so one page usually covers everything; even at 10k rows
    /// it's still 10 requests instead of 100.</summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>Interval between sync cycles.</summary>
    public int SyncIntervalSeconds { get; set; } = 300;

    /// <summary>Per-request timeout for the HttpClient.</summary>
    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Absolute cap on rows pulled per cycle. Guards against a runaway
    /// upstream that reports Total=∞ or a pagination bug that never
    /// advances. Default 100k covers ~600× the current dataset.
    /// </summary>
    public int MaxRowsPerCycle { get; set; } = 100_000;

    public WmsAuthOptions Auth { get; set; } = new();
    public WmsGeofenceOptions Geofence { get; set; } = new();
}

public sealed class WmsAuthOptions
{
    /// <summary>Bearer token for <c>Authorization: Bearer {token}</c>.</summary>
    public string Token { get; set; } = "";
}

/// <summary>
/// WMS PR-3 — geofence config for Manual pickup/drop scanning. WMS
/// locations don't carry a per-row radius, so we apply a fleet-wide
/// default. Ops overrides for specific zones would land here as a
/// zone-code → radius map in a future phase.
/// </summary>
public sealed class WmsGeofenceOptions
{
    /// <summary>Default acceptable radius (meters) around a WMS location's
    /// GPS coord for a pickup / drop scan to be accepted. 30m accommodates
    /// typical GPS jitter on a phone indoors.</summary>
    public double DefaultRadiusM { get; set; } = 30;
}
