using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Application.Security;
using DTMS.Iam.Domain.Entities;
using DTMS.SharedKernel.Caching;
using FluentAssertions;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// Borrowed outbound tokens. The auth service this deployment mints against
// keeps one live token per account, so two systems configured with the same
// mint credentials silently invalidate each other — and nothing notices,
// because the stored exp still reads as valid while every call returns 401.
// The fix is one owner and any number of borrowers.
public class BorrowedTokenTests
{
    private const string OwnerKey = "amr-auth";
    private const string BorrowerKey = "wms";

    // ── Resolution ──────────────────────────────────────────────────────

    [Fact]
    public async Task Borrower_GetsTheOwnersToken()
    {
        var reader = Build(
            owner: Credential(OwnerKey, token: "owner-jwt", refreshConfig: "{}"),
            borrower: Credential(BorrowerKey, token: null, tokenSource: OwnerKey));

        var cred = await reader.GetAsync(BorrowerKey);

        cred!.CallbackAuthConfig.Should().Be("owner-jwt");
    }

    [Fact]
    public async Task Borrower_KeepsItsOwnUrlAndTimeout()
    {
        // Only the token is shared. Pointing a borrower's callbacks at the
        // owner's endpoint would silently misroute them.
        var reader = Build(
            owner: Credential(OwnerKey, token: "owner-jwt", refreshConfig: "{}", baseUrl: "http://owner.test"),
            borrower: Credential(BorrowerKey, token: null, tokenSource: OwnerKey, baseUrl: "http://borrower.test"));

        var cred = await reader.GetAsync(BorrowerKey);

        cred!.CallbackBaseUrl.Should().Be("http://borrower.test");
        cred.TokenSourceKey.Should().Be(OwnerKey);
    }

    [Fact]
    public async Task Owner_IsUnaffected()
    {
        var reader = Build(
            owner: Credential(OwnerKey, token: "owner-jwt", refreshConfig: "{}"),
            borrower: Credential(BorrowerKey, token: null, tokenSource: OwnerKey));

        var cred = await reader.GetAsync(OwnerKey);

        cred!.CallbackAuthConfig.Should().Be("owner-jwt");
        cred.TokenSourceKey.Should().BeNull();
    }

    [Fact]
    public async Task MissingOwner_YieldsNoToken_RatherThanThrowing()
    {
        // The FK makes this unreachable through the API, but a caller that
        // loses its token must fail as a plain 401 from the far side, not as
        // an exception inside the dispatcher.
        var reader = Build(
            owner: null,
            borrower: Credential(BorrowerKey, token: "stale-own-token", tokenSource: OwnerKey));

        var cred = await reader.GetAsync(BorrowerKey);

        cred!.CallbackAuthConfig.Should().BeNull();
    }

    // ── Domain invariants ───────────────────────────────────────────────

    [Fact]
    public void Borrower_CannotAlsoMint()
    {
        var cred = Credential(BorrowerKey, token: null, tokenSource: OwnerKey);

        var act = () => cred.SetTokenRefreshConfig("{}");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*borrows its token from '{OwnerKey}'*");
    }

    [Fact]
    public void Minter_CannotAlsoBorrow()
    {
        var cred = Credential(OwnerKey, token: "t", refreshConfig: "{}");

        var act = () => cred.SetTokenSource("somewhere-else");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*mints its own token*");
    }

    [Fact]
    public void CannotBorrowFromItself()
    {
        var cred = Credential(BorrowerKey, token: null);

        var act = () => cred.SetTokenSource(BorrowerKey);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot borrow its token from itself*");
    }

    [Fact]
    public void ClearingTheLink_ReleasesTheMintingBlock()
    {
        var cred = Credential(BorrowerKey, token: null, tokenSource: OwnerKey);

        cred.SetTokenSource(null);
        cred.SetTokenRefreshConfig("{}");

        cred.TokenSourceKey.Should().BeNull();
        cred.TokenRefreshConfig.Should().Be("{}");
    }

    // ── Fixtures ────────────────────────────────────────────────────────

    private static SystemCredential Credential(
        string key,
        string? token,
        string? refreshConfig = null,
        string? tokenSource = null,
        string baseUrl = "http://example.test")
    {
        var cred = new SystemCredential(key, "bearer-jwt", "{}");
        cred.SetCallback(baseUrl, token is null ? null : "bearer", token);
        if (refreshConfig is not null) cred.SetTokenRefreshConfig(refreshConfig);
        if (tokenSource is not null) cred.SetTokenSource(tokenSource);
        return cred;
    }

    private static CachedCredentialReader Build(SystemCredential? owner, SystemCredential borrower)
    {
        var repo = Substitute.For<ISystemCredentialRepository>();
        repo.GetBySystemKeyAsync(borrower.SystemKey, Arg.Any<CancellationToken>()).Returns(borrower);
        repo.GetBySystemKeyAsync(OwnerKey, Arg.Any<CancellationToken>()).Returns(owner);

        // Always-miss cache: this exercises the repository path, which is where
        // the link is followed. A hit path that skipped resolution would be a
        // bug, so never returning a hit keeps the test honest about that.
        var cache = Substitute.For<ITieredCache>();
        cache.GetAsync<CachedCredential>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedCredential?)null);

        var protector = Substitute.For<ICallbackTokenProtector>();
        protector.Protect(Arg.Any<string?>()).Returns(c => c.Arg<string?>());
        protector.TryUnprotect(Arg.Any<string?>()).Returns(c => c.Arg<string?>());

        return new CachedCredentialReader(cache, repo, protector);
    }
}
