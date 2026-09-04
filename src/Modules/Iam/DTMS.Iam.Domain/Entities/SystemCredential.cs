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

    /// <summary>
    /// Key of another system whose outbound token this one borrows. NULL means
    /// this row owns its token and mints it via <see cref="TokenRefreshConfig"/>.
    ///
    /// <para>Exists because some auth servers keep only one live token per
    /// account: two systems minting with the same credentials would each
    /// invalidate the other's token, and neither would notice — the stored
    /// <c>exp</c> still looks valid, so the refresh loop sees nothing to do
    /// while every call fails 401. Pointing both at one owner means one minter
    /// and no such race.</para>
    ///
    /// <para>Only the token is borrowed. <see cref="CallbackBaseUrl"/>, the
    /// timeout and the resilience settings stay this row's own.</para>
    ///
    /// <para>Mutually exclusive with <see cref="TokenRefreshConfig"/>, and
    /// chains are not allowed — an owner must own its token outright.</para>
    /// </summary>
    public string? TokenSourceKey { get; private set; }

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
        // A borrower must not also mint: that is exactly the two-minters-one-
        // account race TokenSourceKey exists to prevent.
        if (config is not null && TokenSourceKey is not null)
            throw new InvalidOperationException(
                $"System '{SystemKey}' borrows its token from '{TokenSourceKey}'. " +
                "Clear the token source before giving it a refresh config of its own.");

        TokenRefreshConfig = config;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Point this row at another system's token, or pass null to go back to
    /// owning one. The caller is responsible for checking that the target
    /// exists and is itself an owner — this entity can only see itself.
    /// </summary>
    public void SetTokenSource(string? sourceKey)
    {
        if (sourceKey is not null)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                throw new ArgumentException("Token source key cannot be blank.", nameof(sourceKey));
            if (string.Equals(sourceKey, SystemKey, StringComparison.Ordinal))
                throw new InvalidOperationException($"System '{SystemKey}' cannot borrow its token from itself.");
            if (TokenRefreshConfig is not null)
                throw new InvalidOperationException(
                    $"System '{SystemKey}' mints its own token. Clear its refresh config before borrowing from '{sourceKey}'.");
        }

        TokenSourceKey = sourceKey;
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
