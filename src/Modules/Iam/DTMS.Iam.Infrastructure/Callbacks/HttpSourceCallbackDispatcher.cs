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
/// </summary>
public sealed class HttpSourceCallbackDispatcher : ISourceCallbackDispatcher
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly CachedCredentialReader _credReader;
    private readonly ILogger<HttpSourceCallbackDispatcher> _log;

    public HttpSourceCallbackDispatcher(
        HttpClient http,
        CachedCredentialReader credReader,
        ILogger<HttpSourceCallbackDispatcher> log)
    {
        _http = http;
        _credReader = credReader;
        _log = log;
    }

    public async Task DispatchAsync(string systemKey, OutboxMessage message, CancellationToken ct)
    {
        var cred = await _credReader.GetAsync(systemKey, ct)
            ?? throw new InvalidOperationException(
                $"No SystemCredential for '{systemKey}' — cannot dispatch outbound callback. " +
                "This is a configuration error; admin must set CallbackBaseUrl + CallbackAuth* on the credential row.");

        if (string.IsNullOrWhiteSpace(cred.CallbackBaseUrl))
            throw new InvalidOperationException(
                $"SystemCredential for '{systemKey}' has no CallbackBaseUrl. " +
                "Admin must populate it before subscriptions can fire.");

        // Phase S.5 (B2) — honor a per-row route override (already resolved by
        // the formatter, no templating here). Default stays POST /events so
        // every existing subscriber (delivered/cancelled, all systems) is
        // unaffected.
        var path = string.IsNullOrWhiteSpace(message.CallbackPath) ? "/events" : message.CallbackPath!;
        if (!path.StartsWith('/')) path = "/" + path;
        var method = string.IsNullOrWhiteSpace(message.CallbackMethod)
            ? HttpMethod.Post
            : new HttpMethod(message.CallbackMethod!.ToUpperInvariant());

        var url = new Uri(cred.CallbackBaseUrl.TrimEnd('/') + path, UriKind.Absolute);
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

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, linked.Token);
        }
        catch (TaskCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Callback to '{systemKey}' ({url}) timed out after {cred.CallbackTimeoutMs}ms.");
        }

        try
        {
            // 2xx AND 409 Conflict both count as delivered. 409 =
            // "already registered/arrived" (idempotent replay) — matches the
            // legacy OMS adapter's behaviour; treating it as a failure would
            // retry a callback the receiver has already accepted. Applies to
            // every system (delivered/cancelled/erp/sap too — 409 is an
            // idempotent-replay signal regardless of the event).
            if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.Conflict)
                return;

            // Capture the body for the failure log — admin will want it
            // when triaging "why is OMS rejecting our callback?". Bound
            // the read so a misbehaving receiver returning megabytes of
            // HTML can't blow the log line size.
            string? body = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                body = await resp.Content.ReadAsStringAsync(cts.Token);
                if (body.Length > 1000) body = body[..1000] + "…(truncated)";
            }
            catch { /* best-effort logging only */ }

            _log.LogWarning(
                "Callback to system={SystemKey} returned {Status} for outbox row {Id}; body={Body}",
                systemKey, (int)resp.StatusCode, message.Id, body);

            // Throws HttpRequestException carrying StatusCode so the processor
            // can classify permanent (4xx) vs transient (5xx/timeout) for the
            // dispatch-outcome audit.
            resp.EnsureSuccessStatusCode();
        }
        finally
        {
            resp.Dispose();
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
