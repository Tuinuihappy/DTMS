using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using DomainEntities = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

namespace DeliveryOrder.UnitTests;

public class WeightWarningEvaluatorTests
{
    private static DomainEntities.DeliveryOrder OrderWithItems(params (int Seq, string Sku, double? Weight)[] items)
    {
        var order = DomainEntities.DeliveryOrder.Create("ORD-X", Priority.Normal, requestedDeliveryDate: null);
        foreach (var (seq, sku, weight) in items)
            order.AddItem(
                LocationRef.FromCode("WH-01"), LocationRef.FromCode("LINE-01"),
                seq, sku,
                description: null, loadUnitProfileCode: null,
                dimensions: null, weightKg: weight, quantity: 1, uom: "EA",
                cargoType: null, cargoSpecific: null);
        return order;
    }

    [Fact]
    public void AllItemsWithWeight_NoWarnings()
    {
        var order = OrderWithItems((1, "SKU-A", 10.0), (2, "SKU-B", 5.0));

        WeightWarningEvaluator.Evaluate(order.Items).Should().BeEmpty();
    }

    [Fact]
    public void ItemWithNullWeight_EmitsWarning()
    {
        var order = OrderWithItems((1, "SKU-A", null));

        var warnings = WeightWarningEvaluator.Evaluate(order.Items);

        warnings.Should().HaveCount(1);
        warnings[0].Code.Should().Be(QualityIssueCodes.ItemWeightMissing);
        warnings[0].Severity.Should().Be(QualityIssueSeverity.Warning);
        warnings[0].Field.Should().Contain("seq=1");
        warnings[0].Message.Should().Contain("SKU-A");
    }

    [Fact]
    public void OnlySomeItemsMissingWeight_EmitsOnePerMissing()
    {
        var order = OrderWithItems(
            (1, "SKU-A", 10.0),
            (2, "SKU-B", null),
            (3, "SKU-C", 5.0),
            (4, "SKU-D", null));

        var warnings = WeightWarningEvaluator.Evaluate(order.Items);

        warnings.Should().HaveCount(2);
        warnings.Select(w => w.Field).Should().BeEquivalentTo(new[]
        {
            "items[seq=2].weightKg",
            "items[seq=4].weightKg",
        });
    }

    [Fact]
    public void WarningsOrderedByItemSeq()
    {
        var order = OrderWithItems((3, "C", null), (1, "A", null), (2, "B", null));

        var warnings = WeightWarningEvaluator.Evaluate(order.Items);

        warnings.Select(w => w.Field).Should().ContainInOrder(
            "items[seq=1].weightKg",
            "items[seq=2].weightKg",
            "items[seq=3].weightKg");
    }
}

public class DeliveryOrderFallbackWeightTests
{
    [Fact]
    public void ConfirmWithFallback_PublishesEventUsingFallbackForNullWeights()
    {
        var order = DomainEntities.DeliveryOrder.Create("ORD-1", Priority.Normal, requestedDeliveryDate: null);
        order.AddItem(
            LocationRef.FromCode("WH-01"), LocationRef.FromCode("LINE-01"),
            itemSeq: 1, sku: "SKU-A",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: null, quantity: 1, uom: "EA",
            cargoType: null, cargoSpecific: null);
        order.Submit();
        order.MarkAsValidated(new Dictionary<LocationRef, Guid>
        {
            [LocationRef.FromCode("WH-01")] = Guid.NewGuid(),
            [LocationRef.FromCode("LINE-01")] = Guid.NewGuid(),
        });

        order.Confirm(weightFallbackKg: 250.0);

        var evt = order.DomainEvents
            .OfType<AMR.DeliveryPlanning.DeliveryOrder.Domain.Events.DeliveryOrderConfirmedDomainEvent>()
            .Single();
        evt.Items.Single().WeightKg.Should().Be(250.0);
    }

    [Fact]
    public void ConfirmWithFallback_KeepsRealWeightWhenPresent()
    {
        var order = DomainEntities.DeliveryOrder.Create("ORD-2", Priority.Normal, requestedDeliveryDate: null);
        order.AddItem(
            LocationRef.FromCode("WH-01"), LocationRef.FromCode("LINE-01"),
            itemSeq: 1, sku: "SKU-B",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 42.0, quantity: 1, uom: "EA",
            cargoType: null, cargoSpecific: null);
        order.Submit();
        order.MarkAsValidated(new Dictionary<LocationRef, Guid>
        {
            [LocationRef.FromCode("WH-01")] = Guid.NewGuid(),
            [LocationRef.FromCode("LINE-01")] = Guid.NewGuid(),
        });

        order.Confirm(weightFallbackKg: 250.0);

        var evt = order.DomainEvents
            .OfType<AMR.DeliveryPlanning.DeliveryOrder.Domain.Events.DeliveryOrderConfirmedDomainEvent>()
            .Single();
        evt.Items.Single().WeightKg.Should().Be(42.0);
    }
}
