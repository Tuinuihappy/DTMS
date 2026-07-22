using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using DTMS.Iam.Infrastructure.Security;
using DTMS.SharedKernel.Caching;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// Encrypt-at-rest — the reader must never let plaintext CallbackAuthConfig
// reach the cache (Redis persists to disk) and must never hand ciphertext
// to a caller. It also must not mutate the instance the L1 tier holds:
// RedisBackedTieredCache stores the same object reference it was given.
public class CachedCredentialReaderTests
{
    private const string Plaintext = /*lang=json*/ """{"token":"secret-jwt"}""";

    private static readonly CallbackTokenProtector Protector =
        new(new EphemeralDataProtectionProvider());

    private static SystemCredential Entity()
    {
        var e = new SystemCredential("oms", "bearer-jwt", """{"clientSecretHash":"x"}""");
        e.SetCallback("http://oms.local:5002", "bearer", Plaintext);
        return e;
    }

    [Fact]
    public async Task CacheMiss_StoresCiphertext_ButReturnsPlaintext()
    {
        var cache = Substitute.For<ITieredCache>();
        cache.GetAsync<CachedCredential>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedCredential?)null);
        CachedCredential? stored = null;
        await cache.SetAsync(
            Arg.Any<string>(),
            Arg.Do<CachedCredential>(c => stored = c),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        var repo = Substitute.For<ISystemCredentialRepository>();
        repo.GetBySystemKeyAsync("oms", Arg.Any<CancellationToken>()).Returns(Entity());

        var result = await new CachedCredentialReader(cache, repo, Protector).GetAsync("oms");

        result!.CallbackAuthConfig.Should().Be(Plaintext);
        stored.Should().NotBeNull();
        stored!.CallbackAuthConfig.Should().StartWith(CallbackTokenProtector.CiphertextPrefix);
        stored.CallbackAuthConfig.Should().NotContain("secret-jwt");
    }

    [Fact]
    public async Task CacheHit_ReturnsDecryptedCopy_WithoutMutatingCachedInstance()
    {
        var ciphertext = Protector.Protect(Plaintext);
        var cachedInstance = new CachedCredential
        {
            SystemKey = "oms",
            CallbackAuthScheme = "bearer",
            CallbackAuthConfig = ciphertext,
        };
        var cache = Substitute.For<ITieredCache>();
        cache.GetAsync<CachedCredential>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cachedInstance);
        var reader = new CachedCredentialReader(
            cache, Substitute.For<ISystemCredentialRepository>(), Protector);

        var result = await reader.GetAsync("oms");

        result!.CallbackAuthConfig.Should().Be(Plaintext);
        result.Should().NotBeSameAs(cachedInstance);
        cachedInstance.CallbackAuthConfig.Should().Be(ciphertext, "L1 hands back the stored reference — mutating it would leak plaintext into the cache");
    }

    [Fact]
    public async Task CacheHit_PassesThrough_LegacyPlaintextEntry()
    {
        // Entries written before this change (or before the backfill ran)
        // hold plaintext for up to the 5-minute L2 TTL — they must keep
        // working, not throw.
        var cache = Substitute.For<ITieredCache>();
        cache.GetAsync<CachedCredential>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CachedCredential { SystemKey = "oms", CallbackAuthConfig = Plaintext });
        var reader = new CachedCredentialReader(
            cache, Substitute.For<ISystemCredentialRepository>(), Protector);

        (await reader.GetAsync("oms"))!.CallbackAuthConfig.Should().Be(Plaintext);
    }
}
