using DTMS.Api.Infrastructure.Health;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using StackExchange.Redis;

namespace DTMS.Api.UnitTests;

// Readiness must keep each check's severity. Redis and RIOT3 report Degraded
// on purpose — the API still serves — so they must not take the instance out
// of rotation. Postgres, RabbitMQ and drain report Unhealthy and must.
public class HealthResponseTests
{
    [Theory]
    [InlineData(HealthStatus.Healthy, 200)]
    [InlineData(HealthStatus.Degraded, 200)]
    [InlineData(HealthStatus.Unhealthy, 503)]
    public void StatusCode_FollowsSeverity(HealthStatus status, int expected)
        => HealthResponse.StatusCodeFor(status).Should().Be(expected);

    [Fact]
    public void RedisDown_PlusDrain_IsStill503()
    {
        // Aggregate status is the worst entry, so a draining pod stays out of
        // rotation even while Redis is merely Degraded.
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["redis"] = new(HealthStatus.Degraded, "down", TimeSpan.Zero, null, null),
            ["drain"] = new(HealthStatus.Unhealthy, "draining", TimeSpan.Zero, null, null),
        }, TimeSpan.Zero);

        HealthResponse.StatusCodeFor(report.Status).Should().Be(503);
    }

    [Fact]
    public async Task RedisCheck_WhenDisconnected_IsDegradedWithoutTouchingRedis()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.IsConnected.Returns(false);

        var result = await new RedisMultiplexerHealthCheck(redis)
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        redis.DidNotReceiveWithAnyArgs().GetDatabase();
    }

    [Fact]
    public async Task RedisCheck_WhenPingHangs_GivesUpWithinBudget()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.IsConnected.Returns(true);
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.PingAsync(Arg.Any<CommandFlags>()).Returns(new TaskCompletionSource<TimeSpan>().Task);

        var started = DateTime.UtcNow;
        var result = await new RedisMultiplexerHealthCheck(redis)
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(3),
            "the container healthcheck times out at 5 s");
    }
}
