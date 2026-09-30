using DTMS.Api.Middlewares;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Domain.Entities;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Logging;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTMS.Api.UnitTests;

// iam.SystemRequestLog is the only record of what a partner asked us for, and
// between 2026-09-25 and 2026-09-30 it recorded every rejection as a 200: the
// middleware stamped the status as the call unwound, while
// ExceptionHandlingMiddleware — which sits outside it — had not yet mapped the
// exception. ~50 rejections a day landed as successes on endpoints that answer
// 204 when they succeed, and not one 4xx reached the table in five days.
//
// These tests pin the ordering, because it is invisible in either file alone.
public class SystemRequestLoggingMiddlewareTests
{
    [Fact]
    public async Task AThrownError_IsLoggedWithTheStatusTheHandlerMapped()
    {
        var (ctx, startResponse) = Context();
        var writer = new CapturingWriter();
        var logging = new SystemRequestLoggingMiddleware(writer);

        // Nested exactly as Program.cs wires them: exception handling outside,
        // request logging inside the /api/v1/source branch.
        var pipeline = new ExceptionHandlingMiddleware(
            c => logging.InvokeAsync(c, _ => throw new NotFoundException("No delivery order with ref 'OD-1'.")),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await pipeline.InvokeAsync(ctx);

        // The response is built by now, but nothing may have been written yet:
        // stamping early is what produced the 200s.
        writer.Entries.Should().BeEmpty("the row must wait until the status is final");
        ctx.Response.StatusCode.Should().Be(404);

        await startResponse();

        writer.Entries.Should().ContainSingle()
            .Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ARefusal_IsLoggedAs400_NotAsASuccess()
    {
        // The case that actually kept happening in production, on
        // acknowledge-robot-pass and complete.
        var (ctx, startResponse) = Context();
        var writer = new CapturingWriter();
        var logging = new SystemRequestLoggingMiddleware(writer);

        var pipeline = new ExceptionHandlingMiddleware(
            c => logging.InvokeAsync(c, _ => throw new BusinessRuleViolationException(
                "Cannot cancel a trip in Completed status.")),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await pipeline.InvokeAsync(ctx);
        await startResponse();

        writer.Entries.Should().ContainSingle().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ASuccessfulCall_IsLoggedExactlyOnce()
    {
        // Both paths can reach the row on a call that does not throw — the one
        // on the way out, and OnStarting at the flush. Exactly one must win, or
        // every successful request would be counted twice.
        var (ctx, startResponse) = Context();
        var writer = new CapturingWriter();
        var logging = new SystemRequestLoggingMiddleware(writer);

        await logging.InvokeAsync(ctx, c =>
        {
            c.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        writer.Entries.Should().ContainSingle().Which.StatusCode.Should().Be(204);

        await startResponse();

        writer.Entries.Should().HaveCount(1, "the flush must not add a second row");
    }

    [Fact]
    public async Task ACallWithoutASystemPrincipal_IsNotLogged()
    {
        // This middleware is mounted only on the source branch, behind
        // SystemClientAuthMiddleware — but the table is for partner traffic and
        // must not collect anything else if that ever changes.
        var (ctx, startResponse) = Context();
        ctx.Items.Remove(SystemClientAuthMiddleware.PrincipalItemKey);
        var writer = new CapturingWriter();
        var logging = new SystemRequestLoggingMiddleware(writer);

        await logging.InvokeAsync(ctx, _ => Task.CompletedTask);
        await startResponse();

        writer.Entries.Should().BeEmpty();
    }

    // ── harness ──────────────────────────────────────────────────────────

    // Returns the context plus the callback Kestrel would run when the first
    // byte leaves. DefaultHttpContext's own response feature accepts OnStarting
    // registrations and silently drops them, which would hide the ordering
    // these tests exist to check.
    private static (DefaultHttpContext ctx, Func<Task> startResponse) Context()
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Method = "POST",
            Path = "/api/v1/source/trips/4f2a0000-0000-0000-0000-000000000001/cancel",
        });
        var response = new CallbackResponseFeature();
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));

        var ctx = new DefaultHttpContext(features);
        ctx.Items[SystemClientAuthMiddleware.PrincipalItemKey] = new SystemPrincipal("oms", "OMS");
        return (ctx, response.FireOnStartingAsync);
    }

    private sealed class CallbackResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state)
            => _callbacks.Add((callback, state));

        public async Task FireOnStartingAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }

    private sealed class CapturingWriter : IBatchedLogWriter<SystemRequestLogEntry>
    {
        public List<SystemRequestLogEntry> Entries { get; } = [];

        public bool Enqueue(SystemRequestLogEntry entry)
        {
            Entries.Add(entry);
            return true;
        }
    }
}
