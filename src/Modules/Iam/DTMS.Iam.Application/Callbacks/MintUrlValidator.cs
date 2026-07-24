namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// SSRF guard for admin-supplied mint URLs. A <c>tokenUrl</c> comes from admin
/// input and the server makes an outbound POST to it, so it is validated both
/// when saved (<c>PUT /token-refresh</c>) and before every mint call: scheme
/// must be http/https and the host must be on the configured allowlist.
/// </summary>
public static class MintUrlValidator
{
    /// <summary>Returns true when <paramref name="tokenUrl"/> is a well-formed
    /// absolute http/https URL whose host is in <paramref name="allowedHosts"/>.
    /// On false, <paramref name="error"/> explains why (safe to surface to the
    /// admin — it never echoes secrets).</summary>
    public static bool IsAllowed(string? tokenUrl, IReadOnlyCollection<string> allowedHosts, out string? error)
    {
        if (string.IsNullOrWhiteSpace(tokenUrl))
        {
            error = "tokenUrl is required.";
            return false;
        }

        if (!Uri.TryCreate(tokenUrl, UriKind.Absolute, out var uri))
        {
            error = "tokenUrl must be an absolute URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"tokenUrl scheme '{uri.Scheme}' not allowed — only http/https.";
            return false;
        }

        if (allowedHosts is null || allowedHosts.Count == 0)
        {
            error = "No mint hosts are allowlisted — set CallbackTokenRefresh:AllowedMintHosts.";
            return false;
        }

        foreach (var h in allowedHosts)
        {
            if (string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase))
            {
                error = null;
                return true;
            }
        }

        error = $"tokenUrl host '{uri.Host}' is not in the allowlist.";
        return false;
    }
}
