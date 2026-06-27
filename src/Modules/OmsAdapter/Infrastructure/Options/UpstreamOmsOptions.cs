namespace DTMS.OmsAdapter.Infrastructure.Options;

public sealed class UpstreamOmsOptions
{
    public const string SectionName = "UpstreamOms";

    // Kill switch — when false, OmsAdapter is registered but the consumer
    // (Phase 2) skips POSTing. Useful for dev/test environments where the
    // OMS host is unreachable.
    public bool Enabled { get; set; } = false;

    public string BaseUrl { get; set; } = string.Empty;

    // Long-lived JWT (RS256). Put real value in env var
    // UpstreamOms__BearerToken — do not commit to appsettings.
    public string? BearerToken { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}
