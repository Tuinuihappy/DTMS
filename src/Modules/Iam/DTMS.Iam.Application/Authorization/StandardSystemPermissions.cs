namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Permission code templates that every <c>SystemClient</c> is expected to
/// hold once admin onboarding lands (Phase S.4). Templates use the literal
/// <c>{key}</c> placeholder which is substituted with the SystemClient's
/// slug at seed time and again at enforcement time (see
/// <see cref="SourceSystemPermissionHandler"/>).
///
/// <para>Keeping the templates in one place means the resolver and the
/// future auto-seed share a single source of truth — adding a new source
/// surface (e.g. <c>order:cancel</c>) updates both sides.</para>
/// </summary>
public static class StandardSystemPermissions
{
    public const string OrderWriteTemplate = "dtms:source:{key}:order:write";
    public const string OrderReadTemplate  = "dtms:source:{key}:order:read";

    // Trip lifecycle — a source system reports its trips' progress
    // (acknowledge → pickup → drop → complete) through /api/v1/source/trips/*.
    // Granular per-action so a partner can hold, say, drop+complete without
    // acknowledge. Enforced against the caller's own key (SourceSystemPermissionHandler).
    public const string TripAcknowledgeTemplate = "dtms:source:{key}:trip:acknowledge";
    public const string TripPickupTemplate      = "dtms:source:{key}:trip:pickup";
    public const string TripDropTemplate         = "dtms:source:{key}:trip:drop";
    public const string TripCompleteTemplate     = "dtms:source:{key}:trip:complete";

    public static readonly IReadOnlyList<string> All = new[]
    {
        OrderWriteTemplate,
        OrderReadTemplate,
        TripAcknowledgeTemplate,
        TripPickupTemplate,
        TripDropTemplate,
        TripCompleteTemplate,
    };

    /// <summary>
    /// Substitute the <c>{key}</c> placeholder. Caller is responsible for
    /// validating that <paramref name="systemKey"/> is a safe slug
    /// (see <see cref="SourceSystemPermissionHandler.IsValidKey"/>).
    /// </summary>
    public static string Resolve(string template, string systemKey)
        => template.Replace("{key}", systemKey, StringComparison.Ordinal);
}
