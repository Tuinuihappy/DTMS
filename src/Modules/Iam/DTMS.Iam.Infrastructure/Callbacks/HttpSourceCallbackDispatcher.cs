using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Callbacks;
using DTMS.SharedKernel.Outbox;
using Microsoft.Extensions.Logging;

namespace DTMS.Iam.Infrastructure.Callbacks;

/// <summary>
/// Phase S.3.1b — real HTTP implementation of
/// <see cref="ISourceCallbackDispatcher"/>. Replaces the
/// <see cref="LoggingSourceCallbackDispatcher"/> dev stub in DI.
///
/// <para>Per row the dispatcher:
/// <list type="number">
///   <item>Reads the system's <c>CallbackBaseUrl</c> + <c>CallbackAuth*</c>
///         from <see cref="CachedCredentialReader"/>. Falls into a
///         deterministic failure (rethrown for retry) if the system is
///         missing the callback config — that's a config bug, not a
///         transient issue.</item>
///   <item>Constructs <c>POST {CallbackBaseUrl}/events</c> with the
///         outbox row's <c>Content</c> as JSON body, plus
///         <c>X-DTMS-Event-Type</c> + <c>X-DTMS-Event-Id</c> headers
///         so the receiver can dedupe on their side.</item>
///   <item>Applies the outbound auth scheme — Bearer (token from
///         <c>CallbackAuthConfig.token</c>) is the only scheme
///         supported in the MVP. Hmac / mTLS land later.</item>
///   <item><c>EnsureSuccessStatusCode()</c> — any 4xx/5xx surfaces as
///         an exception which the MultiPartitionOutboxProcessor
///         translates into <c>MarkAsFailed</c> + retry per
///         OutboxRetryPolicy.</item>
/// </list>
/// </para>
///
/// <para>2026-08 — a 401 additionally triggers ONE reactive token refresh
/// (<see cref="ICallbackTokenRefresher"/>, force-mint) + immediate retry:
/// receivers like OMS expire sessions on idle time, which the exp-scheduled
/// refresh loop cannot anticipate.</para>
/// </summary>
public sealed class HttpSourceCallbackDispatcher : ISourceCallbackDispatcher
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly CachedCredentialReader _credReader;
    private readonly ICallbackTokenRefresher _tokenRefresher;
    private readonly ILogger<HttpSourceCallbackDispatcher> _log;

    public HttpSourceCallbackDispatcher(
        HttpClient http,
        CachedCredentialReader credReader,
        ICallbackTokenRefresher tokenRefresher,
        ILogger<HttpSourceCallbackDispatcher> log)
    {
        _http = http;
        _credReader = credReader;
        _tokenRefresher = tokenRefresher;
        _log = log;
    }

    public async Task DispatchAsync(string systemKey, OutboxMessage message, CancellationToken ct)
    {
        var cred = await ReadCredAsync(systemKey, ct);

        var resp = await SendOnceAsync(cred, systemKey, message, ct);
        try
        {
            if (IsDelivered(resp)) return;

            // Reactive token refresh (2026-08): receivers like OMS expire
            // sessions on IDLE time, not on the token's exp — so the scheduled
            // refresh loop can't see it coming and the first callback after a
            // quiet hour draws 401. Force-mint once and retry immediately;
            // anything but a successful mint falls through to the normal
            // failure path (the outbox retry ladder picks it up with whatever
            // token the background loop has by then).
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                var refresh = await _tokenRefresher.RefreshAsync(systemKey, force: true, ct);
                if (refresh.Outcome == RefreshOutcome.Refreshed)
                {
                    _log.LogInformation(
                        "Callback to system={SystemKey} drew 401 for outbox row {Id}; token refreshed reactively (new exp={Exp}) — retrying once",
                        systemKey, message.Id, refresh.NewExpiresAt?.ToString("o") ?? "(perpetual)");
                    resp.Dispose();
                    cred = await ReadCredAsync(systemKey, ct);   // refresher invalidated the cache
                    resp = await SendOnceAsync(cred, systemKey, message, ct);
                    if (IsDelivered(resp)) return;
                }
                else
                {
                    _log.LogWarning(
                        "Callback to system={SystemKey} drew 401 for outbox row {Id}; reactive refresh did not mint ({Outcome}: {Message}) — failing normally",
                        systemKey, message.Id, refresh.Outcome, refresh.Message);
                }
            }

            // The body is the only place a receiver explains ITSELF — OMS
            // answers 400 with "FG order is not ready for dropoff arrival.",
            // which tells the operator exactly what to do, while the status
            // alone does not.
            string? body = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                body = Flatten(await resp.Content.ReadAsStringAsync(cts.Token));
            }
            catch { /* best-effort — an unreadable body must not mask the status */ }

            _log.LogWarning(
                "Callback to system={SystemKey} returned {Status} for outbox row {Id}; body={Body}",
                systemKey, (int)resp.StatusCode, message.Id, body);

            // Thrown rather than EnsureSuccessStatusCode() so the body travels
            // with the failure: this message becomes the outbox row's Error AND
            // SourceCallbackOutcome.Detail (MultiPartitionOutboxProcessor), which
            // is what the order timeline shows. EnsureSuccessStatusCode's generic
            // "Response status code does not indicate success" left operators
            // reading container logs to find out why a callback was rejected.
            // StatusCode is preserved because HttpCallbackFailureClassifier keys
            // permanent-vs-transient entirely off it.
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(body)
                    ? $"{(int)resp.StatusCode} {resp.ReasonPhrase} (no response body)"
                    : body,
                inner: null,
                statusCode: resp.StatusCode);
        }
        finally
        {
            resp.Dispose();
        }
    }

    // Receivers answer with anything — JSON, an HTML error page, a multi-line
    // stack trace. Collapse to one line so the log entry and the audit row stay
    // single-line, and bound the length so one misbehaving response can't
    // dominate either (SourceCallbackOutcome.Detail caps again at 400).
    private const int BodyLimit = 1000;

    private static string? Flatten(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var collapsed = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= BodyLimit ? collapsed : collapsed[..BodyLimit] + "…(truncated)";
    }

    // 2xx AND 409 Conflict both count as delivered. 409 = "already
    // registered/arrived" (idempotent replay) — matches the legacy OMS
    // adapter's behaviour; treating it as a failure would retry a callback
    // the receiver has already accepted. Applies to every system.
    private static bool IsDelivered(HttpResponseMessage resp)
        => resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.Conflict;

    private async Task<CachedCredential> ReadCredAsync(string systemKey, CancellationToken ct)
    {
        var cred = await _credReader.GetAsync(systemKey, ct)
            ?? throw new InvalidOperationException(
                $"No SystemCredential for '{systemKey}' — cannot dispatch outbound callback. " +
                "This is a configuration error; admin must set CallbackBaseUrl + CallbackAuth* on the credential row.");

        if (string.IsNullOrWhiteSpace(cred.CallbackBaseUrl))
            throw new InvalidOperationException(
                $"SystemCredential for '{systemKey}' has no CallbackBaseUrl. " +
                "Admin must populate it before subscriptions can fire.");
        return cred;
    }

    // One attempt: build the request from the (possibly re-read) credential
    // and send it. HttpRequestMessage is single-use, so the reactive-refresh
    // retry rebuilds from scratch rather than resending.
    private async Task<HttpResponseMessage> SendOnceAsync(
        CachedCredential cred, string systemKey, OutboxMessage message, CancellationToken ct)
    {
        // Phase S.5 (B2) — honor a per-row route override (already resolved by
        // the formatter, no templating here). Default stays POST /events so
        // every existing subscriber (delivered/cancelled, all systems) is
        // unaffected.
        var path = string.IsNullOrWhiteSpace(message.CallbackPath) ? "/events" : message.CallbackPath!;
        if (!path.StartsWith('/')) path = "/" + path;
        var method = string.IsNullOrWhiteSpace(message.CallbackMethod)
            ? HttpMethod.Post
            : new HttpMethod(message.CallbackMethod!.ToUpperInvariant());

        var url = new Uri(cred.CallbackBaseUrl!.TrimEnd('/') + path, UriKind.Absolute);
        using var req = new HttpRequestMessage(method, url);
        req.Content = new StringContent(message.Content, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("X-DTMS-Event-Type", message.Type);
        req.Headers.TryAddWithoutValidation("X-DTMS-Event-Id", message.Id.ToString());
        if (message.CorrelationId is { } cid)
            req.Headers.TryAddWithoutValidation("X-DTMS-Correlation-Id", cid.ToString());

        ApplyAuth(req, cred);

        // Bound the per-call timeout to the credential's configured value
        // (defaults to 10s). Use a linked cts so caller cancellation also
        // tears down the request.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(cred.CallbackTimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        _log.LogInformation(
            "Dispatching outbox row {Id} (type={Type}) to system={SystemKey} URL={Url}",
            message.Id, message.Type, systemKey, url);

        try
        {
            return await _http.SendAsync(req, linked.Token);
        }
        catch (TaskCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Callback to '{systemKey}' ({url}) timed out after {cred.CallbackTimeoutMs}ms.");
        }
    }

    private static void ApplyAuth(HttpRequestMessage req, CachedCredential cred)
    {
        if (string.IsNullOrWhiteSpace(cred.CallbackAuthScheme))
            return; // No auth — admin opted into "rely on network ACL only".

        switch (cred.CallbackAuthScheme.ToLowerInvariant())
        {
            case "bearer":
                if (cred.CallbackAuthConfig is null) return;
                var bearer = JsonSerializer.Deserialize<BearerConfig>(cred.CallbackAuthConfig, JsonOpts);
                if (string.IsNullOrWhiteSpace(bearer?.Token))
                    throw new InvalidOperationException(
                        $"Callback auth scheme 'bearer' but no token in CallbackAuthConfig for '{cred.SystemKey}'.");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Token);
                break;

            // Hmac / mTLS / signed-request schemes land in a follow-up.
            default:
                throw new NotSupportedException(
                    $"Callback auth scheme '{cred.CallbackAuthScheme}' is not supported. " +
                    "MVP only ships 'bearer'.");
        }
    }

    private sealed class BearerConfig
    {
        public string? Token { get; set; }
    }
}
