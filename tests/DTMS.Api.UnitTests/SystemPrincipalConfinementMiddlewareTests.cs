using System.Security.Claims;
using DTMS.Api.Middlewares;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace DTMS.Api.UnitTests;

public class SystemPrincipalConfinementMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/iam/systems")]
    [InlineData("/api/v1/delivery-orders")]
    [InlineData("/api/v1/operator/trips")]
    [InlineData("/")]
    public async Task SystemPrincipal_OutsideSource_Is403(string path)
    {
        var ctx = ContextFor(path, sub: "system:oms");
        var called = false;

        await new SystemPrincipalConfinementMiddleware()
            .InvokeAsync(ctx, _ => { called = true; return Task.CompletedTask; });

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        called.Should().BeFalse("the wall must reject before the endpoint runs");
    }

    [Theory]
    [InlineData("/api/v1/source/whoami")]
    [InlineData("/api/v1/source/trips/abc/acknowledge")]
    public async Task SystemPrincipal_OnSource_PassesThrough(string path)
    {
        var ctx = ContextFor(path, sub: "system:oms");
        var called = false;

        await new SystemPrincipalConfinementMiddleware()
            .InvokeAsync(ctx, _ => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    // Segment look-alikes must NOT be treated as /api/v1/source/*. These are
    // the exact bypass vectors a naive raw-prefix (StartsWith("/api/v1/source"))
    // would let through — this guards the StartsWithSegments boundary against a
    // future refactor that reopens the wall.
    [InlineData("/api/v1/source-evil/trips")]
    [InlineData("/api/v1/sources")]
    [InlineData("/api/v1/sourcery/whoami")]
    public async Task SystemPrincipal_OnSourceLookalikePath_Is403(string path)
    {
        var ctx = ContextFor(path, sub: "system:oms");
        var called = false;

        await new SystemPrincipalConfinementMiddleware()
            .InvokeAsync(ctx, _ => { called = true; return Task.CompletedTask; });

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        called.Should().BeFalse("a source look-alike segment is not the source surface");
    }

    [Theory]
    // Path matching is OrdinalIgnoreCase — a case-varied source path must still
    // pass so the wall can't be tripped into a false lockout by casing.
    [InlineData("/API/V1/SOURCE/whoami")]
    [InlineData("/Api/V1/Source/trips/abc/acknowledge")]
    public async Task SystemPrincipal_OnSource_CaseInsensitive_PassesThrough(string path)
    {
        var ctx = ContextFor(path, sub: "system:oms");
        var called = false;

        await new SystemPrincipalConfinementMiddleware()
            .InvokeAsync(ctx, _ => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task UserPrincipal_OnAdminPath_PassesThrough()
    {
        // A user (no "system:" sub) is untouched — the wall confines systems only.
        var ctx = ContextFor("/api/v1/iam/systems", sub: "titpooja");
        var called = false;

        await new SystemPrincipalConfinementMiddleware()
            .InvokeAsync(ctx, _ => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_PassesThrough()
    {
        // No principal (e.g. /oauth/token, health, JWKS) → not a system → allowed.
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/oauth/token";
        var called = false;

        await new SystemPrincipalConfinementMiddleware()
            .InvokeAsync(ctx, _ => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
    }

    private static HttpContext ContextFor(string path, string sub)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaim(new Claim("sub", sub));
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }
}
