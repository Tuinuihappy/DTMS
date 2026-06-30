namespace DTMS.OmsAdapter.Abstractions;

/// <summary>
/// Phase S.6 follow-up — resolves the outbound HTTP target (base URL +
/// bearer token + timeout) for a given source-system key at call time.
///
/// <para>Primary source: <c>iam.SystemCredentials.CallbackBaseUrl</c> +
/// <c>.CallbackAuthConfig</c> (jsonb <c>{"token":"..."}</c>) — admin sets
/// via the SystemClient admin UI, no redeploy. Backward-compat fallback:
/// <c>UpstreamOmsOptions</c> (env <c>UpstreamOms__*</c>) so existing
/// deployments keep working until ops moves config to the UI.</para>
///
/// <para>Returns null when neither source produces a usable URL — the
/// caller treats null as "outbound disabled, skip silently". Keeps the
/// dual-flag semantics intact: <c>SystemClient.IsActive</c> still gates
/// inbound auth independently.</para>
/// </summary>
public interface IOmsCallbackTargetResolver
{
    Task<OmsCallbackTarget?> ResolveAsync(string systemKey, CancellationToken cancellationToken);
}

/// <summary>
/// Resolved outbound HTTP target. BearerToken is nullable to support
/// the legacy "no auth" dev setup that just hits the OMS-mock service.
/// </summary>
public sealed record OmsCallbackTarget(string BaseUrl, string? BearerToken, TimeSpan Timeout);
