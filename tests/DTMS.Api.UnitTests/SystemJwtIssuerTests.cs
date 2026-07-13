using System.Security.Cryptography;
using DTMS.Iam.Application.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DTMS.Api.UnitTests;

public class SystemJwtIssuerTests
{
    [Fact]
    public void Issue_Then_Validate_RoundTrip_Succeeds()
    {
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem);

        var token = issuer.Issue("oms");

        token.AccessToken.Should().NotBeNullOrWhiteSpace();
        token.ExpiresInSeconds.Should().Be(3600);
        token.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        var result = ValidateToken(token.AccessToken, publicPem);
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        result.Claims["sub"].Should().Be("system:oms");
        result.Claims["iss"].Should().Be("dtms");
        result.Claims["aud"].Should().Be("dtms-api");
        result.Claims.Should().ContainKey("jti");
    }

    [Fact]
    public void Issue_LifetimeOverride_AppliesToExpClaim()
    {
        var (privatePem, _) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem);

        var token = issuer.Issue("oms", lifetimeSecondsOverride: 60);

        token.ExpiresInSeconds.Should().Be(60);
        token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(60), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Issue_NeverExpires_OmitsExpClaim_AndReportsNullExpiry()
    {
        // Phase S.8d — a perpetual token carries no exp claim (and no
        // expires_in / absolute expiry to report). Everything else — sub,
        // jti, iss, aud, iat, nbf — is stamped as normal.
        var (privatePem, publicPem) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem);

        var token = issuer.Issue("oms", neverExpires: true);

        token.ExpiresAt.Should().BeNull();
        token.ExpiresInSeconds.Should().BeNull();

        var jwt = new JsonWebToken(token.AccessToken);
        jwt.Claims.Should().NotContain(c => c.Type == "exp");
        jwt.Subject.Should().Be("system:oms");
        jwt.Claims.Should().Contain(c => c.Type == "jti");
    }

    [Fact]
    public void Issue_WithDifferentSigningKey_FailsValidation()
    {
        var (privatePem, _) = GenerateRsaKeyPair();
        var (_, foreignPublicPem) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem);

        var token = issuer.Issue("oms");
        var result = ValidateToken(token.AccessToken, foreignPublicPem);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Issue_KidHeader_MatchesOptions()
    {
        var (privatePem, _) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem, keyId: "test-kid-42");

        var token = issuer.Issue("oms");
        var jsonToken = new JsonWebToken(token.AccessToken);

        jsonToken.Kid.Should().Be("test-kid-42");
    }

    [Fact]
    public void Constructor_EmptyPrivateKey_ThrowsLoud()
    {
        var act = () => BuildIssuer(privateKeyPem: "");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PrivateKeyPem is empty*");
    }

    [Fact]
    public void Constructor_MalformedPem_ThrowsWithGuidance()
    {
        var act = () => BuildIssuer(privateKeyPem: "not a pem");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PEM-encoded RSA key*");
    }

    [Fact]
    public void Issue_EmptyKey_Throws()
    {
        var (privatePem, _) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem);

        var act = () => issuer.Issue(systemKey: "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Issue_NegativeLifetime_Throws()
    {
        var (privatePem, _) = GenerateRsaKeyPair();
        using var issuer = BuildIssuer(privatePem);

        var act = () => issuer.Issue("oms", lifetimeSecondsOverride: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static SystemJwtIssuer BuildIssuer(
        string privateKeyPem,
        string keyId = "test-key-v1")
    {
        var opts = Options.Create(new SystemJwtIssuerOptions
        {
            PrivateKeyPem = privateKeyPem,
            Issuer = "dtms",
            Audience = "dtms-api",
            DefaultLifetimeSeconds = 3600,
            KeyId = keyId,
        });
        return new SystemJwtIssuer(opts);
    }

    private static (string PrivatePem, string PublicPem) GenerateRsaKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    private static TokenValidationResult ValidateToken(string token, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var handler = new JsonWebTokenHandler();
        return handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "dtms",
            ValidateAudience = true,
            ValidAudience = "dtms-api",
            ValidateLifetime = true,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ClockSkew = TimeSpan.FromSeconds(5),
        }).GetAwaiter().GetResult();
    }
}

public class ClientSecretGeneratorTests
{
    [Fact]
    public void Mint_Shape_HasPrefix_SlugAndBody()
    {
        var secret = ClientSecretGenerator.Mint("oms");

        secret.Plaintext.Should().StartWith("dtms_cs_oms_");
        secret.Plaintext.Length.Should().BeGreaterThan("dtms_cs_oms_".Length + 40);
    }

    [Fact]
    public void Mint_TwoCalls_ProduceDifferentSecrets()
    {
        var a = ClientSecretGenerator.Mint("oms");
        var b = ClientSecretGenerator.Mint("oms");

        a.Plaintext.Should().NotBe(b.Plaintext);
        a.Sha256Hex.Should().NotBe(b.Sha256Hex);
    }

    [Fact]
    public void Mint_HashRoundTrips_ViaHashPlaintext()
    {
        var secret = ClientSecretGenerator.Mint("oms");
        var rehash = ClientSecretGenerator.HashPlaintext(secret.Plaintext);

        rehash.Should().Be(secret.Sha256Hex);
    }

    [Fact]
    public void Mint_EmptyKey_Throws()
    {
        var act = () => ClientSecretGenerator.Mint("");
        act.Should().Throw<ArgumentException>();
    }
}
