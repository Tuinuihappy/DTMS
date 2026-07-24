namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// Both halves of a system-to-system integration credential, kept
/// together because they share one rotation lifecycle: inbound
/// (how the external system authenticates TO us) and outbound (how
/// we call back to them).
///
/// <para>The <see cref="AuthConfig"/> and <see cref="CallbackAuthConfig"/>
/// jsonb columns hold scheme-specific shapes so we can add bearer-jwt
/// / hmac / api-key without a migration per scheme. Validation lives
/// at the auth-scheme handler, not on this entity.</para>
///
/// <para>Callback fields are nullable for inbound-only integrations
/// (e.g. a third-party that only POSTs to us and we never call back).
/// Resilience defaults match the production checklist (10s callback
/// timeout, 3 retries, breaker opens at 5 failures within 30s).</para>
/// </summary>
public sealed class SystemCredential
{
    public string SystemKey { get; private set; } = string.Empty;
    public string AuthScheme { get; private set; } = string.Empty;
    public string AuthConfig { get; private set; } = string.Empty;
    public string? CallbackBaseUrl { get; private set; }
    public string? CallbackAuthScheme { get; private set; }
    public string? CallbackAuthConfig { get; private set; }
    public int CallbackTimeoutMs { get; private set; } = 10_000;
    public int RetryMaxAttempts { get; private set; } = 3;
    public int CircuitFailureThreshold { get; private set; } = 5;
    public int CircuitDurationSeconds { get; private set; } = 30;

    /// <summary>
    /// Auto-refresh config for the outbound callback bearer token — a JSON
    /// object (encrypted at rest like <see cref="CallbackAuthConfig"/>) holding
    /// the external mint endpoint + credentials + threshold. NULL = no
    /// auto-refresh (the token is rotated manually). Read only by the refresh
    /// background service and admin endpoints, never by the callback hot-path.
    /// </summary>
    public string? TokenRefreshConfig { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Postgres <c>xmin</c> system column, mapped as an optimistic-concurrency
    /// token. Guards against lost updates when two writers (e.g. the manual
    /// "refresh now" endpoint on the API tier and the background refresh loop
    /// on the worker) load and re-save this row via the detached
    /// <c>Update()</c> path, which marks every column modified. A stale value
    /// makes SaveChanges throw <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// instead of silently clobbering the other writer.
    /// </summary>
    public uint Version { get; private set; }

    private SystemCredential() { }

    public SystemCredential(
        string systemKey,
        string authScheme,
        string authConfig)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("SystemKey is required.", nameof(systemKey));
        if (string.IsNullOrWhiteSpace(authScheme))
            throw new ArgumentException("AuthScheme is required.", nameof(authScheme));
        if (string.IsNullOrWhiteSpace(authConfig))
            throw new ArgumentException("AuthConfig is required.", nameof(authConfig));

        SystemKey = systemKey;
        AuthScheme = authScheme;
        AuthConfig = authConfig;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RotateInbound(string authScheme, string authConfig)
    {
        if (string.IsNullOrWhiteSpace(authScheme))
            throw new ArgumentException("AuthScheme is required.", nameof(authScheme));
        if (string.IsNullOrWhiteSpace(authConfig))
            throw new ArgumentException("AuthConfig is required.", nameof(authConfig));
        AuthScheme = authScheme;
        AuthConfig = authConfig;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetCallback(
        string? baseUrl,
        string? authScheme,
        string? authConfig,
        int? timeoutMs = null)
    {
        CallbackBaseUrl = baseUrl;
        CallbackAuthScheme = authScheme;
        CallbackAuthConfig = authConfig;
        if (timeoutMs is { } t)
            CallbackTimeoutMs = t;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Set (or clear, with null) the outbound-token auto-refresh
    /// config. The value is a JSON object; encryption at rest is handled by the
    /// EF value converter, same as <see cref="CallbackAuthConfig"/>.</summary>
    public void SetTokenRefreshConfig(string? config)
    {
        TokenRefreshConfig = config;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateResilience(int retryMaxAttempts, int circuitFailureThreshold, int circuitDurationSeconds)
    {
        if (retryMaxAttempts < 0) throw new ArgumentOutOfRangeException(nameof(retryMaxAttempts));
        if (circuitFailureThreshold < 1) throw new ArgumentOutOfRangeException(nameof(circuitFailureThreshold));
        if (circuitDurationSeconds < 1) throw new ArgumentOutOfRangeException(nameof(circuitDurationSeconds));
        RetryMaxAttempts = retryMaxAttempts;
        CircuitFailureThreshold = circuitFailureThreshold;
        CircuitDurationSeconds = circuitDurationSeconds;
        UpdatedAt = DateTime.UtcNow;
    }
}
