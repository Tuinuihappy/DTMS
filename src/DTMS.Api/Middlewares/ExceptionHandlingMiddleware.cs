using System.Diagnostics;
using DTMS.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DTMS.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Log the full detail with the same traceId the client receives, so
            // a support ticket carrying that id maps straight to this entry.
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            _logger.LogError(ex,
                "An unhandled exception occurred (traceId {TraceId}): {Message}",
                traceId, ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            BusinessRuleViolationException => (StatusCodes.Status400BadRequest, "Business Rule Violation"),
            DomainException => (StatusCodes.Status400BadRequest, "Domain Exception"),
            FluentValidation.ValidationException => (StatusCodes.Status400BadRequest, "Validation Error"),
            // Model-binding failure (malformed JSON body, unknown enum token,
            // bad date …). The framework throws this with StatusCode=400;
            // without this arm it fell to the 500 branch and the caller got
            // a scrubbed "unexpected error" for what is a client-side typo.
            Microsoft.AspNetCore.Http.BadHttpRequestException badReq =>
                (badReq.StatusCode, "Malformed Request"),
            // Optimistic-concurrency clash (xmin token) — another writer changed
            // the row between load and save. A retriable conflict, not a 500.
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Concurrent Update"),
            // Unique-constraint rejection (SQLSTATE 23505) — the sibling of the
            // clash above: someone else took the value between the handler's
            // pre-check and the INSERT. An application-level pre-check can never
            // close that window, so without this arm every duplicate code / key
            // fell to the 500 branch below and the caller got a scrubbed
            // "unexpected error" for what is a plain conflict.
            Microsoft.EntityFrameworkCore.DbUpdateException dbEx
                when Infrastructure.Persistence.PostgresErrors.IsUniqueViolation(dbEx) =>
                (StatusCodes.Status409Conflict, "Duplicate Value"),
            // Mode-disabled is a deployment-configuration outcome, not a
            // server fault — 422 per the IDispatchStrategyRegistry contract,
            // and the message is written for the caller.
            DTMS.Dispatch.Application.Services.TransportModeNotEnabledException =>
                (StatusCodes.Status422UnprocessableEntity, "Transport Mode Not Enabled"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        // Only the modeled exceptions above carry a message written for the
        // caller. Anything reaching the 500 branch is an unexpected internal
        // failure whose message (SQL text, host names, defensive-guard details)
        // must not leave the server — it is logged in InvokeAsync and correlated
        // to the client via traceId instead. This holds in every environment:
        // the box that serves users here runs as Development.
        var isServerError = statusCode == StatusCodes.Status500InternalServerError;
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        // DbUpdateException is the one modeled arm that doesn't author a
        // caller-facing message — EF's own text is "An error occurred while
        // saving the entity changes. See the inner exception for details.",
        // and the inner Postgres message names the violated index, which is
        // schema detail the caller has no use for. Supply a plain one instead;
        // the field-specific wording comes from each handler's own pre-check on
        // the normal path, and this arm only fires when someone won the race.
        var detail = exception switch
        {
            _ when isServerError => "An unexpected error occurred.",
            Microsoft.EntityFrameworkCore.DbUpdateException dbEx
                when Infrastructure.Persistence.PostgresErrors.IsUniqueViolation(dbEx) =>
                "A record with the same unique value already exists.",
            _ => exception.Message
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        // Opaque correlation id so support can find the real error in the logs.
        problemDetails.Extensions["traceId"] = traceId;

        if (exception is FluentValidation.ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .Select(e => new { e.PropertyName, e.ErrorMessage });
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
