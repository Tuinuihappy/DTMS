using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

// Phase 4.3 — Domain-side tests for the eviction signal the gateway
// relies on. The actual WebPushGateway impl lives in Infrastructure and
// is exercised in integration tests; this file pins the contract the
// gateway depends on so a Domain change can't silently break delivery
// state tracking.
public class WebPushSubscriptionEvictionTests
{
    [Fact]
    public void ShouldEvict_FreshSubscription_False()
    {
        var op = Operator.CreateFromJwtClaims("EMP-50", "Test", OperatorRole.Operator);
        op.RegisterPushSubscription(PushPlatform.WebPush,
            endpoint: "https://push.example/a",
            publicKey: "key", authSecret: "secret",
            deviceLabel: "Chrome");

        op.PushSubscriptions.Single().ShouldEvict.Should().BeFalse();
    }

    [Fact]
    public void ShouldEvict_AfterFiveConsecutiveFailures_True()
    {
        var op = Operator.CreateFromJwtClaims("EMP-51", "Test", OperatorRole.Operator);
        op.RegisterPushSubscription(PushPlatform.WebPush,
            endpoint: "https://push.example/b",
            publicKey: "key", authSecret: "secret",
            deviceLabel: "Chrome");
        var sub = op.PushSubscriptions.Single();

        for (var i = 0; i < 5; i++)
            sub.MarkDeliveryFailed();

        sub.ShouldEvict.Should().BeTrue();
    }

    [Fact]
    public void MarkDeliverySucceeded_ResetsFailureCounter()
    {
        var op = Operator.CreateFromJwtClaims("EMP-52", "Test", OperatorRole.Operator);
        op.RegisterPushSubscription(PushPlatform.WebPush,
            endpoint: "https://push.example/c",
            publicKey: "key", authSecret: "secret",
            deviceLabel: "Chrome");
        var sub = op.PushSubscriptions.Single();
        for (var i = 0; i < 3; i++) sub.MarkDeliveryFailed();

        sub.MarkDeliverySucceeded();

        sub.ConsecutiveFailures.Should().Be(0);
        sub.ShouldEvict.Should().BeFalse();
    }
}
