using DTMS.Iam.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

namespace DTMS.Api.UnitTests;

// Encrypt-at-rest — the protector is the single crypto boundary for
// SystemCredentials.CallbackAuthConfig. The CfDJ8-prefix discrimination is
// what keeps legacy plaintext rows readable (pre-backfill) and makes both
// operations idempotent, so it gets pinned here.
public class CallbackTokenProtectorTests
{
    private static CallbackTokenProtector Build()
        => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        var protector = Build();
        const string plaintext = /*lang=json*/ """{"token":"eyJhbGciOi..."}""";

        var ciphertext = protector.Protect(plaintext);

        ciphertext.Should().StartWith(CallbackTokenProtector.CiphertextPrefix);
        ciphertext.Should().NotContain("eyJhbGciOi");
        protector.TryUnprotect(ciphertext).Should().Be(plaintext);
    }

    [Fact]
    public void Protect_IsIdempotent_OnCiphertext()
    {
        var protector = Build();
        var once = protector.Protect("""{"token":"abc"}""");

        protector.Protect(once).Should().Be(once);
    }

    [Fact]
    public void TryUnprotect_PassesThrough_LegacyPlaintext()
    {
        const string legacy = """{"token":"stored-before-encryption"}""";

        Build().TryUnprotect(legacy).Should().Be(legacy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BothOperations_PassThrough_NullAndEmpty(string? value)
    {
        var protector = Build();

        protector.Protect(value).Should().Be(value);
        protector.TryUnprotect(value).Should().Be(value);
    }

    [Fact]
    public void TryUnprotect_ThrowsWithRecoveryHint_WhenKeyRingLost()
    {
        // Two ephemeral providers = two unrelated key rings — same shape as
        // "the dp-keys volume was deleted and a fresh key was generated".
        var ciphertext = Build().Protect("""{"token":"abc"}""");
        var differentKeyRing = Build();

        var act = () => differentKeyRing.TryUnprotect(ciphertext);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Re-save the token via the admin Configure UI*");
    }
}
