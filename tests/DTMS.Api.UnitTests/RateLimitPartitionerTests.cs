using System.Security.Claims;
using DTMS.Api.Infrastructure.RateLimiting;
using DTMS.Api.Middlewares;
using DTMS.Iam.Application.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace DTMS.Api.UnitTests;

/// <summary>
/// Which quota a request spends. The property that matters throughout: two
/// different callers never share a partition, and the classes that must never
/// be refused never get one.
/// </summary>
public class RateLimitPartitionerTests
{
    private static HttpContext Ctx(
        string path,
        ClaimsPrincipal? user = null,
        SystemPrincipal? system = null,
        string ip = "10.0.0.5")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        if (user is not null) ctx.User = user;
        if (system is not null) ctx.Items[SystemClientAuthMiddleware.PrincipalItemKey] = system;
        return ctx;
    }

    private static ClaimsPrincipal SignedIn(string sub) =>
        new(new ClaimsIdentity([new Claim("sub", sub)], authenticationType: "jwt"));

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/metrics")]
    [InlineData("/hubs/trips")]
    [InlineData("/.well-known/jwks.json")]
    public void Infrastructure_IsNeverLimited(string path)
        => RateLimitPartitioner.Resolve(Ctx(path)).Class.Should().Be(TrafficClass.Infra);

    // A rejected webhook frame is lost until the reconciler notices, so this
    // class exists to be exempt.
    [Fact]
    public void VendorWebhooks_AreTheirOwnClass()
        => RateLimitPartitioner.Resolve(Ctx("/api/webhooks/riot3/notify/secret"))
            .Class.Should().Be(TrafficClass.Webhook);

    [Fact]
    public void SignedInUsers_GetTheirOwnPartition()
    {
        var a = RateLimitPartitioner.Resolve(Ctx("/api/v1/dispatch/trips", SignedIn("111")));
        var b = RateLimitPartitioner.Resolve(Ctx("/api/v1/dispatch/trips", SignedIn("222")));

        a.Class.Should().Be(TrafficClass.User);
        b.Class.Should().Be(TrafficClass.User);
        a.Should().NotBe(b, "one user's burst must not spend another's quota");
    }

    // The whole point of the redesign: same proxy address, different callers.
    [Fact]
    public void TwoUsersBehindOneProxyAddress_DoNotShareAPartition()
    {
        var a = RateLimitPartitioner.Resolve(Ctx("/api/v1/delivery-orders", SignedIn("111"), ip: "172.18.0.9"));
        var b = RateLimitPartitioner.Resolve(Ctx("/api/v1/delivery-orders", SignedIn("222"), ip: "172.18.0.9"));

        a.Key.Should().NotBe(b.Key);
    }

    [Fact]
    public void SystemClients_ArePartitionedByPrincipal()
    {
        var key = RateLimitPartitioner.Resolve(Ctx(
            "/api/v1/source/delivery-orders",
            system: new SystemPrincipal("oms", "OMS")));

        key.Class.Should().Be(TrafficClass.System);
        key.Key.Should().Be("system:oms");
    }

    // The system principal is set by middleware that runs after the user JWT
    // scheme; if both are somehow present the system identity is the caller.
    [Fact]
    public void SystemPrincipal_WinsOverAUserToken()
        => RateLimitPartitioner.Resolve(Ctx(
                "/api/v1/source/whoami",
                user: SignedIn("111"),
                system: new SystemPrincipal("oms", "OMS")))
            .Class.Should().Be(TrafficClass.System);

    [Theory]
    [InlineData("/api/v1/fleet/attachments/carrier/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/thumbnail")]
    [InlineData("/api/v1/fleet/attachments/carrier-type/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/image")]
    public void AttachmentImages_HaveTheirOwnClass_StillPerCaller(string path)
    {
        var key = RateLimitPartitioner.Resolve(Ctx(path, SignedIn("111")));

        key.Class.Should().Be(TrafficClass.AttachmentImage);
        key.Key.Should().Be("user:111");
    }

    [Fact]
    public void OtherAttachmentRoutes_AreOrdinaryUserTraffic()
        => RateLimitPartitioner.Resolve(Ctx(
                "/api/v1/fleet/attachments/carrier/11111111-1111-1111-1111-111111111111",
                SignedIn("111")))
            .Class.Should().Be(TrafficClass.User);

    [Fact]
    public void WithoutAToken_TheAddressIsTheCaller()
    {
        var key = RateLimitPartitioner.Resolve(Ctx("/api/v1/dispatch/trips", ip: "10.0.0.9"));

        key.Class.Should().Be(TrafficClass.Anonymous);
        key.Key.Should().Be("ip:10.0.0.9");
    }

    // A system JWT presented outside /api/v1/source is 403'd before the limiter,
    // but if that ever changed it must not be credited as a user.
    [Fact]
    public void ASystemSubWithoutTheMiddleware_IsNotAUser()
        => RateLimitPartitioner.Resolve(Ctx("/api/v1/dispatch/trips", SignedIn("system:oms")))
            .Class.Should().Be(TrafficClass.Anonymous);

    [Fact]
    public void ResolvedKey_IsReusedForTheRestOfTheRequest()
    {
        var ctx = Ctx("/api/v1/dispatch/trips", SignedIn("111"));
        ctx.Items[RateLimitPartitioner.ItemKey] = new RateLimitPartitionKey(TrafficClass.System, "system:oms");

        RateLimitPartitioner.For(ctx).Should().Be(new RateLimitPartitionKey(TrafficClass.System, "system:oms"));
    }
}
