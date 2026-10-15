using System.Collections.Concurrent;
using DTMS.Api.Infrastructure.TokenRefresh;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Application.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// The background sweep that keeps outbound callback tokens (oms, wms) fresh.
// It used to share one scope — one refresher, one DbContext — across systems
// refreshed in parallel, so whichever hit the DB second threw "A second
// operation was started on this context instance" and that system missed the
// hour. These pin the per-system scope, failure isolation and the fast retry.
public class CallbackTokenRefreshServiceTests
{
    private readonly ConcurrentBag<ICallbackTokenRefresher> _refreshersCreated = new();
    private readonly ConcurrentDictionary<string, Func<RefreshResult>> _behaviour = new();

    private CallbackTokenRefreshService Service(params string[] keys)
    {
        var repo = Substitute.For<ISystemCredentialRepository>();
        repo.ListKeysWithTokenRefreshAsync(Arg.Any<CancellationToken>()).Returns(keys.ToList());

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        services.AddScoped(_ =>
        {
            var refresher = Substitute.For<ICallbackTokenRefresher>();
            refresher.RefreshAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(_behaviour[ci.ArgAt<string>(0)]()));
            _refreshersCreated.Add(refresher);
            return refresher;
        });
        var provider = services.BuildServiceProvider();

        var options = Substitute.For<IOptionsMonitor<CallbackTokenRefreshOptions>>();
        options.CurrentValue.Returns(new CallbackTokenRefreshOptions { MaxParallelism = 4 });

        return new CallbackTokenRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<CallbackTokenRefreshService>.Instance);
    }

    [Fact]
    public async Task EachSystem_GetsItsOwnRefresher_SoNoDbContextIsShared()
    {
        _behaviour["oms"] = () => RefreshResult.Refreshed(DateTime.UtcNow.AddHours(3));
        _behaviour["wms"] = () => RefreshResult.Refreshed(DateTime.UtcNow.AddHours(3));

        var anyFailed = await Service("oms", "wms").SweepOnceAsync(CancellationToken.None);

        anyFailed.Should().BeFalse();
        _refreshersCreated.Should().HaveCount(2, "one scope per system, never one shared across the parallel refreshes");
        _refreshersCreated.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task OneSystemThrowing_DoesNotStopTheOther_AndFlagsTheSweep()
    {
        var omsRefreshed = false;
        _behaviour["oms"] = () =>
        {
            omsRefreshed = true;
            return RefreshResult.Refreshed(DateTime.UtcNow.AddHours(3));
        };
        _behaviour["wms"] = () => throw new InvalidOperationException("db down");

        var anyFailed = await Service("oms", "wms").SweepOnceAsync(CancellationToken.None);

        anyFailed.Should().BeTrue();
        omsRefreshed.Should().BeTrue();
    }

    [Fact]
    public async Task MintFailure_FlagsTheSweep()
    {
        _behaviour["oms"] = () => RefreshResult.Failed("Mint failed: 503");

        (await Service("oms").SweepOnceAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task NotDue_IsNotAFailure()
    {
        _behaviour["oms"] = () => RefreshResult.Skipped("Not due yet.");

        (await Service("oms").SweepOnceAsync(CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task NoSystems_IsNotAFailure()
    {
        (await Service().SweepOnceAsync(CancellationToken.None)).Should().BeFalse();
        _refreshersCreated.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, 3600, 300, 3600)]  // all good → normal interval
    [InlineData(true, 3600, 300, 300)]    // a failure → retry soon
    [InlineData(true, 120, 300, 120)]     // retry never waits longer than the interval
    public void NextDelay_RetriesSoonerAfterAFailure(bool anyFailed, int poll, int retry, int expectedSeconds)
    {
        var opts = new CallbackTokenRefreshOptions { PollIntervalSeconds = poll, RetryAfterFailureSeconds = retry };

        CallbackTokenRefreshService.NextDelay(opts, anyFailed).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
