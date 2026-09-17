using System.Security.Claims;
using DTMS.Api.Auth;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

/// <summary>
/// ResolveUserId decides who stamped an audit row and whose rate-limit quota a
/// request spends. Each arm is pinned here because a quiet change in precedence
/// would re-attribute history rather than fail loudly.
/// </summary>
public class ClaimsPrincipalResolveUserIdTests
{
    private static ClaimsPrincipal With(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "test"));

    [Fact]
    public void EmployeeId_WinsOverEverythingElse()
        => With(("EmployeeId", "111"), ("employeeCode", "222"), (ClaimTypes.Name, "bob"), ("sub", "333"))
            .ResolveUserId().Should().Be("111");

    [Fact]
    public void EmployeeCode_IsUsedWhenThereIsNoEmployeeId()
        => With(("employeeCode", "222"), (ClaimTypes.Name, "bob"), ("sub", "333"))
            .ResolveUserId().Should().Be("222");

    [Fact]
    public void IdentityName_IsUsedWhenThereIsNoEmployeeClaim()
        => With((ClaimTypes.Name, "bob"), ("sub", "333"))
            .ResolveUserId().Should().Be("bob");

    // The real External Auth LDAP token: identity lives only in sub.
    [Fact]
    public void Sub_IsTheFallbackForRealLdapTokens()
        => With(("sub", "86347852"), ("unique_name", "someone"))
            .ResolveUserId().Should().Be("86347852");

    [Fact]
    public void SystemSub_IsNeverAUser()
        => With(("sub", "system:oms"))
            .ResolveUserId().Should().BeNull();

    // Preserved quirk of the original chain: ?? only skips null, so a present
    // but empty EmployeeId stops the claim chain — and then falls to sub.
    [Fact]
    public void EmptyEmployeeId_SkipsTheOtherClaims_ButStillFallsBackToSub()
        => With(("EmployeeId", ""), ("employeeCode", "222"), ("sub", "333"))
            .ResolveUserId().Should().Be("333");

    [Fact]
    public void NoIdentifyingClaim_ReturnsNothing()
        => new ClaimsPrincipal(new ClaimsIdentity()).ResolveUserId()
            .Should().Match<string?>(s => string.IsNullOrWhiteSpace(s));
}
