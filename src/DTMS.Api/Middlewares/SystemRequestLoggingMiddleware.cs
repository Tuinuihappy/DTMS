using System.Diagnostics;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Domain.Entities;
using DTMS.SharedKernel.Logging;

namespace DTMS.Api.Middlewares;

/// <summary>
/// Records every authenticated request from a <see cref="SystemPrincipal"/>
/// to the partitioned <c>iam.SystemRequestLog</c> table. The row is enqueued
/// into the batched-log writer so the hot request path never waits on a DB
/// INSERT — the drain service bulk-inserts in 200-row chunks (or every 5
/// seconds).
/// </summary>
/// <remarks>
/// <para>The status is read from <see cref="HttpResponse.OnStarting"/> rather
/// than on the way out of <c>next</c>, because <see cref="ExceptionHandlingMiddleware"/>
/// sits OUTSIDE this one: a handler that throws unwinds past here with the
/// status still at its 200 default, and only afterwards does that middleware
/// map the exception to 404 / 403 / 400 and write the body.</para>
///
/// <para>Stamping on the way out therefore recorded every thrown error as a
/// success. It went unnoticed until 2026-09-30 because it only began on
/// 2026-09-25, when the source endpoints stopped returning
/// <c>Results.BadRequest</c> and started throwing instead; the table had not
/// held a single 4xx in the five days since, while ~50 rejections a day kept
/// arriving as 200s on endpoints that answer 204 when they succeed.</para>
///
/// <para>Duration is therefore measured to the start of the response, not to
/// the end of the body.</para>
/// </remarks>
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
        var slot = new WriteOnce();

        context.Response.OnStarting(() =>
        {
            Record(context, sw, slot);
            return Task.CompletedTask;
        });

        var faulted = false;
        try
        {
            await next(context);
        }
        catch
        {
            // Leave the row to OnStarting. ExceptionHandlingMiddleware catches
            // everything and always writes a response, so the callback runs
            // with the mapped status; recording from the finally below would
            // re-create the 200 this class exists to avoid.
            faulted = true;
            throw;
        }
        finally
        {
            // Ordinary path: nothing is flushed yet (a 204 carries no body) but
            // the status is already final because the endpoint set it. Taking
            // the slot here keeps the guarantee that a completed call always
            // leaves a row even if the connection dies before the flush —
            // OnStarting then finds the slot gone.
            if (!faulted)
            {
                Record(context, sw, slot);
            }
        }
    }

    private void Record(HttpContext context, Stopwatch sw, WriteOnce slot)
    {
        if (!slot.Take())
        {
            return;
        }

        if (context.Items[SystemClientAuthMiddleware.PrincipalItemKey] is not SystemPrincipal sp)
        {
            return;
        }

        sw.Stop();

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

    /// <summary>One row per request, whichever path reaches it first.</summary>
    private sealed class WriteOnce
    {
        private int _taken;

        public bool Take() => Interlocked.Exchange(ref _taken, 1) == 0;
    }
}
