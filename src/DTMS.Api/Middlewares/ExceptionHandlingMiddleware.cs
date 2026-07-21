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

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = isServerError ? "An unexpected error occurred." : exception.Message,
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
