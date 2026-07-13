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
