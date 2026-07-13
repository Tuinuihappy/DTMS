using DTMS.Api.Auth;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.SharedKernel.Caching;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace DTMS.Api.UnitTests;

public class OrderOriginResolverTests
{
    [Fact]
    public async Task GetInternalAsync_ReturnsConstant_WithoutTouchingSystemClients()
    {
        // Phase S.8d follow-up — the internal origin is decoupled from the
        // SystemClients registry: GetInternalAsync returns a fixed pair and
        // never opens the cache/DB, so a fresh (empty) registry is fine.
        var repo = Substitute.For<ISystemClientRepository>();
        var reader = new CachedSystemClientReader(Substitute.For<ITieredCache>(), repo);
        var resolver = new OrderOriginResolver(reader, Substitute.For<IHttpContextAccessor>());

        var origin = await resolver.GetInternalAsync();

        origin.Key.Should().Be("internal");
        origin.DisplayName.Should().Be("Internal");
        await repo.DidNotReceive().GetByKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
