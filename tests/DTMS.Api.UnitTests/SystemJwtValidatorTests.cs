using System.Security.Cryptography;
using DTMS.Iam.Application.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DTMS.Api.UnitTests;

public class SystemJwtValidatorTests
{
    [Fact]
    public void Validate_HappyPath_ReturnsKeyStrippedOfPrefix()
    {
        var (issuer, validator) = BuildPair();
        var token = issuer.Issue("oms");

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeTrue(result.FailureReason);
        result.SystemKey.Should().Be("oms");
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Validate_WrongSigningKey_Fails()
    {
        var (foreignIssuer, _) = BuildPair();
        var (_, ourValidator) = BuildPair();
        var token = foreignIssuer.Issue("oms");

        var result = ourValidator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
        result.SystemKey.Should().BeNull();
    }

    [Fact]
    public void Validate_ExpiredToken_Fails()
    {
        var (issuer, validator) = BuildPair();
        // 1-second lifetime, sleep past it
        var token = issuer.Issue("oms", lifetimeSecondsOverride: 1);
        Thread.Sleep(TimeSpan.FromSeconds(32)); // 1s exp + 30s clock skew + buffer

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WrongAudience_Fails()
    {
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        var issuer = BuildIssuer(privatePem, audience: "some-other-api");
        var validator = BuildValidator(publicPem, audience: "dtms-api");
        var token = issuer.Issue("oms");

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WrongIssuer_Fails()
    {
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        var issuer = BuildIssuer(privatePem, issuerName: "rogue-issuer");
        var validator = BuildValidator(publicPem, issuerName: "dtms");
        var token = issuer.Issue("oms");

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_EmptyToken_FailsWithoutInvokingHandler(string token)
    {
        var (_, validator) = BuildPair();

        var result = validator.Validate(token);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("empty token");
    }

    [Fact]
    public void Validate_MalformedToken_Fails()
    {
        var (_, validator) = BuildPair();

        var result = validator.Validate("not.a.jwt");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Constructor_EmptyPublicKey_ThrowsLoud()
    {
        var act = () => BuildValidator(publicKeyPem: "");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PublicKeyPem is empty*");
    }

    [Fact]
    public void Constructor_MalformedPem_ThrowsWithGuidance()
    {
        var act = () => BuildValidator(publicKeyPem: "garbage");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PEM-encoded RSA key*");
    }

    // ── Phase S.8c — revocation flow (integration-style) ────────────────

    [Fact]
    public void Validate_TokenOnRevocationList_ReturnsRejected()
    {
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        var revocationList = Substitute.For<ISystemJwtRevocationList>();
        var issuer = BuildIssuer(privatePem);
        var token = issuer.Issue("oms");

        // Stub blocklist to say "yes, this jti is revoked".
        revocationList.IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        var validator = new SystemJwtValidator(
            Options.Create(new SystemJwtIssuerOptions
            {
                PublicKeyPem = publicPem,
                Issuer = "dtms",
                Audience = "dtms-api",
                KeyId = "test-v1",
            }),
            revocationList,
            NullLogger<SystemJwtValidator>.Instance);

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("token revoked");
    }

    [Fact]
    public void Validate_RevocationListThrows_FailsClosed()
    {
        // Fail-close semantics: Redis outage → reject request. The
        // alternative (fail-open) would let a revoked-but-leaked token
        // slip through during a Redis blip, defeating the point of
        // per-jti revocation.
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        var revocationList = Substitute.For<ISystemJwtRevocationList>();
        revocationList.IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("simulated Redis outage"));

        var issuer = BuildIssuer(privatePem);
        var token = issuer.Issue("oms");
        var validator = new SystemJwtValidator(
            Options.Create(new SystemJwtIssuerOptions
            {
                PublicKeyPem = publicPem,
                Issuer = "dtms",
                Audience = "dtms-api",
                KeyId = "test-v1",
            }),
            revocationList,
            NullLogger<SystemJwtValidator>.Instance);

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("revocation check unavailable");
    }

    [Fact]
    public void Validate_RevocationListNotCalled_WhenSignatureAlreadyFailed()
    {
        // Ordering matters: don't hit Redis for tokens that already failed
        // signature validation — cheaper checks come first.
        var (foreignPriv, _) = GenerateRsaKeyPair();
        var (_, ourPub) = GenerateRsaKeyPair();
        var foreignIssuer = BuildIssuer(foreignPriv);
        var token = foreignIssuer.Issue("oms");

        var revocationList = Substitute.For<ISystemJwtRevocationList>();
        var validator = new SystemJwtValidator(
            Options.Create(new SystemJwtIssuerOptions
            {
                PublicKeyPem = ourPub,
                Issuer = "dtms",
                Audience = "dtms-api",
                KeyId = "test-v1",
            }),
            revocationList,
            NullLogger<SystemJwtValidator>.Instance);

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeFalse();
        // Signature check fails before we reach the revocation list.
        revocationList.DidNotReceive().IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validate_JtiPassedToRevocationCheck_MatchesTokenJti()
    {
        // Verifies the validator hands the correct jti to the blocklist —
        // a bug where we pass the wrong claim (e.g. sub) would silently
        // allow revoked tokens through until observed in prod.
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        string? capturedJti = null;
        var revocationList = Substitute.For<ISystemJwtRevocationList>();
        revocationList.IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { capturedJti = ci.Arg<string>(); return Task.FromResult(false); });

        var issuer = BuildIssuer(privatePem);
        var token = issuer.Issue("oms");
        var validator = new SystemJwtValidator(
            Options.Create(new SystemJwtIssuerOptions
            {
                PublicKeyPem = publicPem,
                Issuer = "dtms",
                Audience = "dtms-api",
                KeyId = "test-v1",
            }),
            revocationList,
            NullLogger<SystemJwtValidator>.Instance);

        var result = validator.Validate(token.AccessToken);

        result.IsValid.Should().BeTrue();
        capturedJti.Should().Be(token.Jti);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static (SystemJwtIssuer Issuer, SystemJwtValidator Validator) BuildPair()
    {
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        return (BuildIssuer(privatePem), BuildValidator(publicPem));
    }

    private static SystemJwtIssuer BuildIssuer(
        string privateKeyPem,
        string issuerName = "dtms",
        string audience = "dtms-api")
    {
        var opts = Options.Create(new SystemJwtIssuerOptions
        {
            PrivateKeyPem = privateKeyPem,
            Issuer = issuerName,
            Audience = audience,
            DefaultLifetimeSeconds = 3600,
            KeyId = "test-v1",
        });
        return new SystemJwtIssuer(opts);
    }

    private static SystemJwtValidator BuildValidator(
        string publicKeyPem,
        string issuerName = "dtms",
        string audience = "dtms-api")
    {
        var opts = Options.Create(new SystemJwtIssuerOptions
        {
            PublicKeyPem = publicKeyPem,
            Issuer = issuerName,
            Audience = audience,
            KeyId = "test-v1",
        });
        // Phase S.8c — validator now checks a revocation list. Tests
        // that don't care about revocation use a stub that always
        // returns false ("not revoked"). Revocation-specific tests can
        // wire their own stub via BuildValidator overload if needed.
        var revocationList = Substitute.For<ISystemJwtRevocationList>();
        revocationList.IsRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        return new SystemJwtValidator(opts, revocationList, NullLogger<SystemJwtValidator>.Instance);
    }

    private static (string PrivatePem, string PublicPem) GenerateRsaKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
}
