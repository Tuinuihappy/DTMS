using System.Diagnostics;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Domain.Entities;
using DTMS.SharedKernel.Logging;

namespace DTMS.Api.Middlewares;

/// <summary>
/// Records every authenticated request from a <see cref="SystemPrincipal"/>
/// to the partitioned <c>iam.SystemRequestLog</c> table. Runs AFTER
/// the endpoint handler — Stopwatch captures the total handling time,
/// and the row is enqueued into the batched-log writer so the hot
/// request path never waits on a DB INSERT. The drain service bulk-
/// inserts in 200-row chunks (or every 5 seconds).
/// </summary>
public sealed class SystemRequestLoggingMiddleware : IMiddleware
{
    private readonly IBatchedLogWriter<SystemRequestLogEntry> _writer;

    public SystemRequestLoggingMiddleware(IBatchedLogWriter<SystemRequestLogEntry> writer)
    {
        _writer = writer;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();

            if (context.Items[SystemClientAuthMiddleware.PrincipalItemKey] is SystemPrincipal sp)
            {
                // Idempotency-Key is a free-form header (no typed property
                // on HttpRequest.Headers) — use the indexer.
                var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();

                // Prefer the W3C trace id (cross-service correlation);
                // fall back to ASP.NET's per-request TraceIdentifier.
                var correlationId = Activity.Current?.TraceId.ToString()
                    ?? context.TraceIdentifier;

                _writer.Enqueue(new SystemRequestLogEntry(
                    id: Guid.NewGuid(),
                    occurredAt: DateTime.UtcNow,
                    systemKey: sp.Key,
                    method: context.Request.Method,
                    path: context.Request.Path,
                    statusCode: context.Response.StatusCode,
                    idempotencyKey: idempotencyKey,
                    correlationId: correlationId,
                    durationMs: (int)sw.ElapsedMilliseconds));
            }
        }
    }
}
