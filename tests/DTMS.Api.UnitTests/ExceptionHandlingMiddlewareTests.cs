using System.Text.Json;
using DTMS.Api.Middlewares;
using DTMS.SharedKernel.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTMS.Api.UnitTests;

// Guards the information-disclosure fix: an unexpected 500 must never carry the
// real exception text to the caller, while modeled 4xx errors — whose messages
// are written for the user — must pass through unchanged. Every response also
// carries a traceId so a support ticket maps back to the server log.
public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Unexpected500_HidesRealMessage_AndAddsTraceId()
    {
        var body = await RunWith(new InvalidOperationException(
            "42703: column s.CargoSpecific_Weight does not exist; Host=dtms-postgres"));

        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        body.GetProperty("detail").GetString().Should().NotContain("42703");
        body.GetProperty("detail").GetString().Should().NotContain("dtms-postgres");
        body.TryGetProperty("traceId", out var tid).Should().BeTrue();
        tid.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InnerExceptionText_IsNotLeaked()
    {
        var body = await RunWith(new Exception("outer",
            new Exception("SECRET connection string in inner")));

        var json = body.GetRawText();
        json.Should().NotContain("SECRET");
        json.Should().NotContain("inner");
    }

    [Fact]
    public async Task NotFound_KeepsItsUserFacingMessage()
    {
        var body = await RunWith(new NotFoundException("OrderTemplate 40c3 not found."));

        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("detail").GetString().Should().Be("OrderTemplate 40c3 not found.");
        body.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task BusinessRuleViolation_KeepsItsUserFacingMessage()
    {
        var body = await RunWith(new BusinessRuleViolationException("Priority must be 0-9."));

        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("detail").GetString().Should().Be("Priority must be 0-9.");
    }

    [Fact]
    public async Task Validation_KeepsFieldErrors()
    {
        var failure = new FluentValidation.Results.ValidationFailure("Priority", "must be 0-9");
        var body = await RunWith(new FluentValidation.ValidationException(new[] { failure }));

        body.GetProperty("status").GetInt32().Should().Be(400);
        body.TryGetProperty("errors", out var errors).Should().BeTrue(
            "field-level validation detail must survive so the form can show it");
        errors.GetArrayLength().Should().Be(1);
    }

    // Drives the middleware with a pipeline that throws, then parses the JSON
    // body it wrote to the response.
    private static async Task<JsonElement> RunWith(Exception thrown)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/v1/test";
        ctx.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw thrown,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ctx.Response.Body);
        var text = await reader.ReadToEndAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
