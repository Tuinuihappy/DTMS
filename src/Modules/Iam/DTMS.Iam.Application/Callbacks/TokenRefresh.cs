using System.Text.Json;
using System.Text.Json.Serialization;

namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Parsed shape of <c>SystemCredential.TokenRefreshConfig</c> — how the
/// auto-refresh loop mints a fresh outbound callback token for one system.
/// Stored as an encrypted JSON object; <see cref="Password"/> is a secret and
/// must never be logged or returned in a metadata summary.
/// </summary>
public sealed record TokenRefreshSettings
{
    /// <summary>External mint endpoint, e.g. the WMS <c>/auth/login</c>. POSTed
    /// with <c>{username, password}</c>. Must pass <see cref="MintUrlValidator"/>.</summary>
    [JsonPropertyName("tokenUrl")]
    public string TokenUrl { get; init; } = "";

    [JsonPropertyName("username")]
    public string Username { get; init; } = "";

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>Dotted path to the token in the mint response, e.g. <c>token</c>
    /// or <c>data.token</c>. Default <c>token</c>.</summary>
    [JsonPropertyName("tokenField")]
    public string TokenField { get; init; } = "token";

    /// <summary>Refresh once the current token's remaining lifetime drops below
    /// this. Default 8h.</summary>
    [JsonPropertyName("refreshBeforeSeconds")]
    public int RefreshBeforeSeconds { get; init; } = 28_800;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Parse the stored JSON config; null when empty or malformed.</summary>
    public static TokenRefreshSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<TokenRefreshSettings>(json, JsonOpts); }
        catch { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
}

/// <summary>What a single refresh attempt did.</summary>
public enum RefreshOutcome
{
    /// <summary>Token replaced with a freshly minted one.</summary>
    Refreshed,
    /// <summary>Not due yet, disabled, no config, or perpetual on the auto path.</summary>
    Skipped,
    /// <summary>Mint call failed or returned an unusable token — old token kept.</summary>
    Failed,
    /// <summary>Refused on purpose (e.g. would downgrade a perpetual token, or the
    /// minted token is not strictly newer than the current one).</summary>
    Rejected,
    /// <summary>Another writer holds the per-system lock right now.</summary>
    LockBusy,
}

public sealed record RefreshResult(RefreshOutcome Outcome, DateTime? NewExpiresAt = null, string? Message = null)
{
    public static RefreshResult Refreshed(DateTime? exp) => new(RefreshOutcome.Refreshed, exp);
    public static RefreshResult Skipped(string? why = null) => new(RefreshOutcome.Skipped, null, why);
    public static RefreshResult Failed(string why) => new(RefreshOutcome.Failed, null, why);
    public static RefreshResult Rejected(string why) => new(RefreshOutcome.Rejected, null, why);
    public static RefreshResult LockBusy() => new(RefreshOutcome.LockBusy, null, "Another refresh is in progress for this system.");
}

/// <summary>Whether a refresh should mint, and whether a minted token is kept.</summary>
public enum MintDecision
{
    /// <summary>Proceed to mint a new token.</summary>
    Mint,
    /// <summary>Nothing to do — not due, or a perpetual token on the auto path.</summary>
    Skip,
    /// <summary>Refuse — a forced refresh would downgrade a perpetual token.</summary>
    RejectPerpetual,
}

/// <summary>
/// The pure decision logic of a refresh, factored out so it can be unit-tested
/// without Redis, EF or HTTP. <see cref="ICallbackTokenRefresher"/>
/// implementations call these to decide whether to mint and whether to keep
/// what they minted.
/// </summary>
public static class RefreshPolicy
{
    /// <summary>Decide whether to mint given the current token state.</summary>
    /// <param name="hasCurrentToken">A bearer token is currently stored.</param>
    /// <param name="currentExp">Its exp, or null for perpetual / no token.</param>
    /// <param name="force">Manual "refresh now" bypasses the due check.</param>
    public static MintDecision Evaluate(
        bool hasCurrentToken, DateTime? currentExp, bool force, int refreshBeforeSeconds, DateTime nowUtc)
    {
        // Perpetual = a token exists but carries no exp.
        if (hasCurrentToken && currentExp is null)
            return force ? MintDecision.RejectPerpetual : MintDecision.Skip;

        if (!force && currentExp is DateTime exp)
        {
            var remaining = exp - nowUtc;
            if (remaining > TimeSpan.FromSeconds(refreshBeforeSeconds))
                return MintDecision.Skip;
        }

        return MintDecision.Mint;
    }

    /// <summary>Keep a minted token only when it is an improvement: a later exp,
    /// or itself perpetual (null newExp). Reject when both have an exp and the
    /// new one is not strictly later.</summary>
    public static bool AcceptsMinted(DateTime? currentExp, DateTime? newExp)
        => !(currentExp is DateTime cur && newExp is DateTime fresh && fresh <= cur);
}

/// <summary>Mints a fresh bearer token from an external system's token endpoint.</summary>
public interface ICallbackTokenMinter
{
    /// <summary>POST the mint endpoint and return the bare JWT string, or throw
    /// on a network/HTTP/parse failure. Never logs the token or password.</summary>
    Task<string> MintAsync(TokenRefreshSettings settings, CancellationToken ct);
}

/// <summary>Refreshes the outbound callback token for one system, end to end:
/// acquire the per-system lock, reload fresh, decide, mint, persist, invalidate.
/// The single code path shared by the background loop and the manual endpoint.</summary>
public interface ICallbackTokenRefresher
{
    Task<RefreshResult> RefreshAsync(string systemKey, bool force, CancellationToken ct);
}
