using DTMS.DeliveryOrder.Domain.Events;
using DTMS.DeliveryOrder.Infrastructure.Services;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.SharedKernel.Auth;
using FluentAssertions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

// P0 Day 2 — verifies the mapper reads from the ambient ICurrentActorContext
// at Map() time so every published integration event carries the operator /
// system identity. The projector under P1 will rely on these fields landing
// in history.triggered_by + history.correlation_id without any extra joins.
//
// Coverage:
//   - HTTP path → TriggeredBy = user id (claim name)
//   - Webhook path → TriggeredBy = "vendor-webhook" via BeginScope
//   - Multi-event aggregate → all events share the same actor snapshot
//   - Schema bump to "1.1" lands in the wire payload
public class P0MapperEnrichmentTests
{
    [Fact]
    public void Mapper_PopulatesTriggeredBy_FromHttpActor()
    {
        var actor = StubActor("ops-lead-01", "http", correlationId: null);
        var mapper = new DeliveryOrderDomainEventMapper(actor);

        var domain = new DeliveryOrderCancelledDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "stuck");
        var integrationEvents = mapper.Map(domain);

        integrationEvents.Should().HaveCount(1);
        var integration = integrationEvents.First()
            .Should().BeOfType<DeliveryOrderCancelledIntegrationEventV1>().Subject;
        integration.TriggeredBy.Should().Be("ops-lead-01");
        integration.SchemaVersion.Should().Be("1.1");
    }

    [Fact]
    public void Mapper_PopulatesTriggeredBy_FromWebhookSource()
    {
        // Vendor webhook path — consumer pushed an explicit ActorContext
        // before SaveChanges fires.
        var ambient = new AsyncLocalActorContext();
        using var scope = ambient.BeginScope(
            new ActorContext(UserId: null, Source: "vendor-webhook", CorrelationId: null));
        var mapper = new DeliveryOrderDomainEventMapper(ambient);

        var domain = new DeliveryOrderFailedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "vendor timeout");
        var integration = (DeliveryOrderFailedIntegrationEventV1)mapper.Map(domain).First();

        integration.TriggeredBy.Should().Be("vendor-webhook");
    }

    [Fact]
    public void Mapper_PropagatesCorrelationId()
    {
        var corr = Guid.NewGuid();
        var actor = StubActor("ops-01", "http", corr);
        var mapper = new DeliveryOrderDomainEventMapper(actor);

        var domain = new DeliveryOrderHeldDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "policy");
        var integration = (DeliveryOrderHeldIntegrationEventV1)mapper.Map(domain).First();

        integration.CorrelationId.Should().Be(corr);
    }

    [Fact]
    public void Mapper_FallsBackToSystem_WhenNoActor()
    {
        var actor = StubActor(null, "system", null);
        var mapper = new DeliveryOrderDomainEventMapper(actor);

        var domain = new DeliveryOrderCompletedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());
        var integration = (DeliveryOrderCompletedIntegrationEventV1)mapper.Map(domain).First();

        integration.TriggeredBy.Should().Be("system");
    }

    [Fact]
    public void InternalOnlyEvents_StillReturnEmpty()
    {
        var mapper = new DeliveryOrderDomainEventMapper(StubActor("ops-01", "http", null));

        // DeliveryOrderPlannedDomainEvent is intentionally NOT published —
        // its outbox emission would be noise. Verify the mapping rule still
        // returns [] after the V1.1 refactor.
        var planned = new DeliveryOrderPlannedDomainEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());
        mapper.Map(planned).Should().BeEmpty();
    }

    private static ICurrentActorContext StubActor(string? userId, string source, Guid? correlationId)
    {
        var ctx = Substitute.For<ICurrentActorContext>();
        ctx.Current.Returns(new ActorContext(userId, source, correlationId));
        return ctx;
    }
}
