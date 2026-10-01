using System.Net.Http.Json;
using System.Text.Json;
using DTMS.Iam.Application.Callbacks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// HTTP implementation of <see cref="ICallbackTokenMinter"/>. POSTs
/// <c>{username, password}</c> to the system's mint endpoint and reads the JWT
/// from the response's top-level <c>token</c> property.
///
/// <para>Only that one shape is supported. A configurable field path used to
/// exist and no system ever set it — every mint endpoint in use answers with
/// <c>{"token": "…"}</c> — so it was removed rather than carried as an untested
/// option. A system answering some other shape needs code here, not config.</para>
///
/// <para>The typed <see cref="HttpClient"/> is registered with a hard-ceiling
/// timeout and <c>AllowAutoRedirect=false</c> so a mint endpoint cannot bounce
/// the request to an unvetted host. The URL is also re-checked against the SSRF
/// allowlist here (defence in depth — the PUT endpoint checks it too).</para>
/// </summary>
public sealed class HttpCallbackTokenMinter : ICallbackTokenMinter
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<CallbackTokenRefreshOptions> _options;
    private readonly ILogger<HttpCallbackTokenMinter> _log;

    public HttpCallbackTokenMinter(
        HttpClient http,
        IOptionsMonitor<CallbackTokenRefreshOptions> options,
        ILogger<HttpCallbackTokenMinter> log)
    {
        _http = http;
        _options = options;
        _log = log;
    }

    public async Task<string> MintAsync(TokenRefreshSettings settings, CancellationToken ct)
    {
        var allow = _options.CurrentValue.AllowedMintHosts;
        if (!MintUrlValidator.IsAllowed(settings.TokenUrl, allow, out var urlError))
            throw new InvalidOperationException($"Mint URL rejected: {urlError}");

        // Body carries the secret — never logged. Only the URL/host is logged.
        using var req = new HttpRequestMessage(HttpMethod.Post, settings.TokenUrl)
        {
            Content = JsonContent.Create(new { username = settings.Username, password = settings.Password }),
        };

        _log.LogInformation("Minting outbound token from {Host}", new Uri(settings.TokenUrl).Host);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Mint endpoint returned {(int)resp.StatusCode} {resp.ReasonPhrase}.", null, resp.StatusCode);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var token = ReadTopLevelToken(doc.RootElement);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Mint response had no top-level string 'token' property. Only "
                + "{\"token\": \"…\"} is supported — a nested token needs a code change here.");

        return token;
    }

    private static string? ReadTopLevelToken(JsonElement root)
        => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("token", out var tok)
            && tok.ValueKind == JsonValueKind.String
                ? tok.GetString()
                : null;
}
