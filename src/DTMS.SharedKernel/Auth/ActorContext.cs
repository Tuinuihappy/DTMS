namespace DTMS.SharedKernel.Auth;

/// <summary>
/// Snapshot of "who triggered this operation, from where, on whose behalf"
/// carried through the request pipeline + ambient context.
/// </summary>
/// <remarks>
/// <para>Phase S.1 widened this from a 3-field record (UserId, Source,
/// CorrelationId) into a structured principal + channel + on-behalf-of
/// triple so audits can distinguish "Titichai cancelled via the web" from
/// "OMS cancelled on behalf of John". The old shape is preserved via
/// derived getters and a legacy constructor so the 16 call sites that
/// already read <see cref="TriggeredBy"/> / <see cref="CorrelationId"/>
/// — projectors, integration-event mappers, admin endpoints, tests —
/// keep working without a touch.</para>
/// <para><b>PrincipalId format.</b> Stable, prefixed string identifying
/// who acted: <c>user:{EmployeeId}</c> for human callers, <c>system:{key}</c>
/// for federated source-system callers (S.2+). Anonymous calls yield
/// the empty string so <see cref="TriggeredBy"/> falls back to the
/// channel name — never silently null in the audit row.</para>
/// </remarks>
public sealed record ActorContext
{
    /// <summary>
    /// Stable identifier prefixed with the principal kind:
    /// <c>user:{EmployeeId}</c> or <c>system:{systemKey}</c>. Empty when
    /// the caller is anonymous (rare — most endpoints require auth).
    /// </summary>
    public string PrincipalId { get; init; } = string.Empty;

    public PrincipalType Type { get; init; } = PrincipalType.User;

    /// <summary>Display name from the JWT (<c>name</c> / <c>DisplayName</c>) or system DisplayName row.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public SourceChannel Channel { get; init; } = SourceChannel.InternalJob;

    /// <summary>
    /// Populated when a system principal acts on behalf of an external
    /// user — e.g. OMS forwards a request that John triggered in the OMS
    /// UI. Endpoint handlers parse the optional <c>sourceUser</c> body
    /// field (S.2+) and push a richer scope; null for direct user calls.
    /// </summary>
    public UserReference? OnBehalfOf { get; init; }

    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Legacy free-form source label preserved so existing audit rows
    /// stay comparable across the schema change. Populated automatically
    /// from <see cref="Channel"/> on the new ctor; passed through verbatim
    /// on the legacy ctor.
    /// </summary>
    public string Source { get; init; } = "system";

    /// <summary>
    /// Back-compat alias. Returns <see cref="PrincipalId"/> stripped of
    /// the kind prefix (so old projectors stamping <c>history.triggered_by</c>
    /// keep getting the bare employee id). Null when anonymous.
    /// </summary>
    public string? UserId
    {
        get
        {
            if (string.IsNullOrEmpty(PrincipalId)) return null;
            int colon = PrincipalId.IndexOf(':');
            return colon >= 0 ? PrincipalId[(colon + 1)..] : PrincipalId;
        }
    }

    /// <summary>
    /// The string that lands in <c>history.triggered_by</c>. Prefers the
    /// bare user/system id; falls back to the channel-derived source
    /// label so the row is never silently null.
    /// </summary>
    public string TriggeredBy
    {
        get
        {
            var u = UserId;
            return string.IsNullOrWhiteSpace(u) ? Source : u;
        }
    }

    /// <summary>System default — used when no ambient/HTTP context exists.</summary>
    public static ActorContext System { get; } = new()
    {
        PrincipalId = string.Empty,
        Type = PrincipalType.User,
        DisplayName = "system",
        Channel = SourceChannel.InternalJob,
        OnBehalfOf = null,
        CorrelationId = null,
        Source = "system",
    };

    /// <summary>Default ctor reserved for the record-init / static initializer paths.</summary>
    public ActorContext() { }

    /// <summary>
    /// Legacy 3-arg constructor preserved for existing call sites
    /// (tests, the pre-S.1 HTTP resolver, MassTransit consumer scopes).
    /// PascalCase parameter names are retained from the original positional
    /// record so the existing <c>new ActorContext(UserId: …, Source: …)</c>
    /// call sites keep compiling without a touch.
    /// </summary>
    public ActorContext(string? UserId, string Source, Guid? CorrelationId)
    {
        PrincipalId = string.IsNullOrWhiteSpace(UserId) ? string.Empty : $"user:{UserId}";
        Type = PrincipalType.User;
        DisplayName = UserId ?? Source;
        Channel = MapLegacySource(Source);
        this.Source = Source;
        this.CorrelationId = CorrelationId;
    }

    /// <summary>
    /// Structured constructor used by the S.1+ HTTP resolver. The legacy
    /// <see cref="Source"/> string is derived from <paramref name="channel"/>
    /// so old projection rows render in the same vocabulary.
    /// </summary>
    public ActorContext(
        string principalId,
        PrincipalType type,
        string displayName,
        SourceChannel channel,
        UserReference? onBehalfOf,
        Guid? correlationId)
    {
        PrincipalId = principalId ?? string.Empty;
        Type = type;
        DisplayName = displayName ?? string.Empty;
        Channel = channel;
        OnBehalfOf = onBehalfOf;
        CorrelationId = correlationId;
        Source = channel switch
        {
            SourceChannel.SystemApi => "system-api",
            SourceChannel.OperatorPwa => "operator-pwa",
            SourceChannel.InternalJob => "scheduled-job",
            _ => "http",
        };
    }

    private static SourceChannel MapLegacySource(string source) => source switch
    {
        "operator-pwa" => SourceChannel.OperatorPwa,
        "vendor-webhook" => SourceChannel.SystemApi,
        "scheduled-job" => SourceChannel.InternalJob,
        "system" => SourceChannel.InternalJob,
        _ => SourceChannel.ManualWeb,
    };
}

public enum PrincipalType
{
    User,
    System,
}

public enum SourceChannel
{
    /// <summary>Operator using the desktop web UI (manual transport workflows).</summary>
    ManualWeb,
    /// <summary>Driver/operator using the mobile PWA at <c>/api/operator/*</c>.</summary>
    OperatorPwa,
    /// <summary>Federated external system calling <c>/api/v1/source/{key}/*</c> (S.2+).</summary>
    SystemApi,
    /// <summary>Background hosted services, outbox consumers, scheduled jobs.</summary>
    InternalJob,
}

/// <summary>
/// Lightweight pointer to a user record in an external system. Used in
/// <see cref="ActorContext.OnBehalfOf"/> so the audit row can show
/// "OMS acted on behalf of ext:OMS-USER-42 (John Smith)" without
/// requiring DTMS to model the external user lifecycle.
/// </summary>
public sealed record UserReference(string ExternalId, string? DisplayName);
