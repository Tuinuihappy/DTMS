using System.Text.Json;

namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Signature-less inspection of outbound callback bearer tokens. Pure decode,
/// no validation — used to surface expiry to admins and to let the auto-refresh
/// loop decide when a token is near its <c>exp</c>.
///
/// <para>Two shapes are handled: the <b>stored config</b> JSON
/// (<c>{"token":"&lt;jwt&gt;"}</c>) that lives in
/// <c>SystemCredentials.CallbackAuthConfig</c>, and a <b>bare JWT</b> string as
/// returned straight from an external mint endpoint. Both ultimately route
/// through <see cref="ReadExpFromBareJwt"/> so the base64url/exp logic exists
/// once.</para>
/// </summary>
public static class CallbackTokenInspector
{
    /// <summary>Extract the bearer token string from a stored
    /// <c>{"token":"…"}</c> config. Null for empty or malformed config.</summary>
    public static string? ReadStoredToken(string? callbackAuthConfig)
    {
        if (string.IsNullOrWhiteSpace(callbackAuthConfig)) return null;
        try
        {
            using var doc = JsonDocument.Parse(callbackAuthConfig);
            if (!doc.RootElement.TryGetProperty("token", out var tokenEl)) return null;
            if (tokenEl.ValueKind != JsonValueKind.String) return null;
            var token = tokenEl.GetString();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decode the <c>exp</c> of the JWT stored inside a
    /// <c>{"token":"…"}</c> config. Null when the config is empty/malformed, the
    /// token is not a JWT, or it carries no <c>exp</c> claim (a perpetual token).</summary>
    public static DateTime? ReadExpiryFromConfig(string? callbackAuthConfig)
        => ReadExpFromBareJwt(ReadStoredToken(callbackAuthConfig));

    /// <summary>Decode the <c>exp</c> (as UTC) of a bare JWT string — the shape
    /// an external mint endpoint returns. Null on any decode failure or when
    /// there is no <c>exp</c> claim. Does NOT verify the signature.</summary>
    public static DateTime? ReadExpFromBareJwt(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            // base64url → base64 (RFC 7515): replace -/_ with +/, re-pad
            var payloadB64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payloadB64.Length % 4) { case 2: payloadB64 += "=="; break; case 3: payloadB64 += "="; break; }
            var payloadBytes = Convert.FromBase64String(payloadB64);

            using var payloadDoc = JsonDocument.Parse(payloadBytes);
            if (!payloadDoc.RootElement.TryGetProperty("exp", out var expEl)) return null;
            if (expEl.ValueKind != JsonValueKind.Number) return null;
            var expSeconds = expEl.GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
        }
        catch
        {
            // Any decode/parse failure → null. Auxiliary info, not a hard
            // requirement — callers show "no expiry info" rather than error.
            return null;
        }
    }
}
