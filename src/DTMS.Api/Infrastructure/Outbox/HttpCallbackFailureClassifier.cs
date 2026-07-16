using System.Net;
using DTMS.SharedKernel.Outbox;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Classifies dispatch failures for partitioned (HTTP callback) outbox rows.
/// Permanent = the receiver deterministically rejected the payload; retrying
/// the identical request can never succeed and only head-blocks the partition
/// (per-partition ordering — a poison row would otherwise burn the full
/// OutboxRetryPolicy backoff, ~2h45m, in front of every good callback).
///
/// Everything else keeps the OutboxRetryPolicy backoff — including null
/// StatusCode (connection-level DNS/refused/TLS), 401/403/404 (admin-fixable
/// config or token; retries auto-heal once the credential row is fixed),
/// 408/429, all 5xx, and every non-HTTP exception (timeouts, missing
/// CallbackBaseUrl, cache errors). 409 never reaches this code — the
/// dispatcher treats it as delivered (idempotent replay).
/// </summary>
internal static class HttpCallbackFailureClassifier
{
    internal static bool IsPermanent(HttpStatusCode? statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => true,            // 400 — e.g. OMS duplicate shipment.started (create-once API)
        HttpStatusCode.MethodNotAllowed => true,      // 405 — verb baked into the row
        HttpStatusCode.Gone => true,                  // 410
        HttpStatusCode.RequestEntityTooLarge => true, // 413 — payload baked into the row
        HttpStatusCode.UnsupportedMediaType => true,  // 415
        HttpStatusCode.UnprocessableEntity => true,   // 422
        _ => false,
    };

    /// <summary>
    /// Applies the classified failure to the row. Returns true if the row
    /// went terminal (permanent). The "[permanent NNN]" Error prefix is the
    /// ops-triage tag distinguishing fast-terminal rows from retry-exhausted
    /// ones in admin output and raw SQL. Pure w.r.t. I/O — unit-testable
    /// without Postgres or EF.
    /// </summary>
    internal static bool ApplyFailure(OutboxMessage msg, Exception failure, DateTime attemptedAtUtc)
    {
        var status = (failure as HttpRequestException)?.StatusCode;
        if (IsPermanent(status))
        {
            msg.MarkAsPermanentlyFailed(
                attemptedAtUtc,
                $"[permanent {(int)status!.Value}] {failure.Message}");
            return true;
        }

        msg.MarkAsFailed(attemptedAtUtc, failure.Message);
        return false;
    }
}
