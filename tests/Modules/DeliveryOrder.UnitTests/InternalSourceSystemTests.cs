using DTMS.DeliveryOrder.Domain.Enums;
using FluentAssertions;
using DomainEntities = DTMS.DeliveryOrder.Domain.Entities;

namespace DTMS.DeliveryOrder.UnitTests;

/// <summary>
/// Guards the 'manual' → 'internal' source-system rename: UI/operator orders
/// are stamped 'internal', and the upstream factory still refuses that slug.
/// </summary>
public class InternalSourceSystemTests
{
    [Fact]
    public void WellKnown_Internal_HasExpectedSlugAndDisplayName()
    {
        DTMS.DeliveryOrder.Domain.WellKnownSourceSystems.Internal.Should().Be("internal");
        DTMS.DeliveryOrder.Domain.WellKnownSourceSystems.InternalDisplayName.Should().Be("Internal");
    }

    [Fact]
    public void Create_Defaults_StampInternalOrigin()
    {
        var order = DomainEntities.DeliveryOrder.Create(
            "REF-1", Priority.Normal, serviceWindow: null);

        order.SourceSystemKey.Should().Be("internal");
        order.SourceSystemDisplayName.Should().Be("Internal");
    }

    [Fact]
    public void CreateFromUpstream_InternalKey_Throws()
    {
        // 'internal' is the UI path — an upstream system must never claim it.
        var act = () => DomainEntities.DeliveryOrder.CreateFromUpstream(
            "REF-2", Priority.Normal, serviceWindow: null,
            sourceSystemKey: DTMS.DeliveryOrder.Domain.WellKnownSourceSystems.Internal,
            sourceSystemDisplayName: "Internal");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*internal*");
    }

    [Fact]
    public void CreateFromUpstream_ExternalKey_Succeeds()
    {
        var order = DomainEntities.DeliveryOrder.CreateFromUpstream(
            "REF-3", Priority.Normal, serviceWindow: null,
            sourceSystemKey: "oms", sourceSystemDisplayName: "OMS");

        order.SourceSystemKey.Should().Be("oms");
        order.SourceSystemDisplayName.Should().Be("OMS");
    }
}
