using System.Security.Claims;
using DTMS.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace DTMS.Api.UnitTests;

/// <summary>
/// Real External Auth LDAP tokens name the user in <c>sub</c> only. The
/// accessor stopped at EmployeeId ?? Identity.Name, so every real user came
/// back null — CreatedBy on internal orders and templates, a trip cancel's
/// ActionBy and the resend attribution were blank (found 2026-10-23).
/// </summary>
public class HttpContextCurrentUserAccessorTests
{
    [Fact]
    public void LdapToken_WithOnlySub_ReturnsTheEmployeeId()
        => Accessor(new Claim("sub", "86347852")).GetCurrentUserName().Should().Be("86347852");

    [Fact]
    public void EmployeeIdClaim_WinsOverSub()
        => Accessor(new Claim("EmployeeId", "E1"), new Claim("sub", "86347852"))
            .GetCurrentUserName().Should().Be("E1");

    [Fact]
    public void SystemPrincipal_ResolvesToItsDisplayName()
        => Accessor(new Claim(ClaimTypes.Name, "OMS"), new Claim("sub", "system:oms"))
            .GetCurrentUserName().Should().Be("OMS");

    [Fact]
    public void SystemSub_AloneIsNeverAUser()
        => Accessor(new Claim("sub", "system:oms")).GetCurrentUserName().Should().BeNull();

    [Fact]
    public void NoHttpContext_ReturnsNull()
        => new HttpContextCurrentUserAccessor(new HttpContextAccessor()).GetCurrentUserName().Should().BeNull();

    private static HttpContextCurrentUserAccessor Accessor(params Claim[] claims)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };
        return new HttpContextCurrentUserAccessor(new HttpContextAccessor { HttpContext = http });
    }
}
