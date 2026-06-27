using DTMS.DeliveryOrder.Application.Commands.AmendDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using DomainEntities = DTMS.DeliveryOrder.Domain.Entities;

namespace DeliveryOrder.UnitTests;

public class OrderSnapshotV1Tests
{
    private static DomainEntities.DeliveryOrder BuildRichOrder()
    {
        var order = DomainEntities.DeliveryOrder.Create(
            "SNAP-001", Priority.High,
            serviceWindow: ServiceWindow.Create(
                earliestUtc: new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
                latestUtc:   new DateTime(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc)),
            sourceSystem: SourceSystem.Manual,
            createdBy: "ops-user",
            requestedBy: "production-line-3",
            notes: "VIP shipment");

        order.AddItem(
            "WH-COLD-01", "LAB-FREEZER",
            itemSeq: 1, itemId: "VACCINE-LOT-A-001",
            description: "Refrigerated batch",
            loadUnitProfileCode: "TRAY-A",
            dimensions: Dimensions.Create(300, 200, 100),
            weightKg: 2.5,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            hazmat: HazmatInfo.Create("6.2", PackingGroup.II),
            temperature: TemperatureRange.Create(2, 8),
            handlingInstructions: new[]
            {
                HandlingInstruction.Fragile,
                HandlingInstruction.ThisSideUp
            });

        return order;
    }

    [Fact]
    public void SchemaVersion_IsFrozenAt_1_0()
    {
        // Bump deliberately when the snapshot shape evolves; the column-level
        // AmendmentVersion must bump in lock-step.
        OrderSnapshotV1.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void From_CapturesAllMutableOrderFields()
    {
        var order = BuildRichOrder();

        var snap = OrderSnapshotV1.From(order);

        snap.Version.Should().Be("1.0");
        snap.OrderRef.Should().Be("SNAP-001");
        snap.Priority.Should().Be("High");
        snap.OrderStatus.Should().Be("Draft");
        snap.RequestedBy.Should().Be("production-line-3");
        snap.Notes.Should().Be("VIP shipment");
        snap.ServiceWindow.Should().NotBeNull();
        snap.ServiceWindow!.EarliestUtc.Should().Be(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        snap.ServiceWindow.LatestUtc.Should().Be(new DateTime(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc));
        snap.Items.Should().HaveCount(1);
    }

    [Fact]
    public void From_CapturesAllItemValueObjects()
    {
        var order = BuildRichOrder();

        var snap = OrderSnapshotV1.From(order);
        var item = snap.Items.Single();

        item.ItemId.Should().Be("VACCINE-LOT-A-001");
        item.Quantity.Uom.Should().Be("BOX");
        item.Hazmat.Should().NotBeNull();
        item.Hazmat!.ClassCode.Should().Be("6.2");
        item.Hazmat.PackingGroup.Should().Be("II");
        item.Temperature.Should().NotBeNull();
        item.Temperature!.MinC.Should().Be(2);
        item.Temperature.MaxC.Should().Be(8);
        item.HandlingInstructions.Should().BeEquivalentTo(new[] { "Fragile", "ThisSideUp" });
    }

    [Fact]
    public void From_OrderWithNoServiceWindow_HasNullSnapshotField()
    {
        var order = DomainEntities.DeliveryOrder.Create(
            "NO-WINDOW", Priority.Normal, serviceWindow: null,
            sourceSystem: SourceSystem.Manual, createdBy: null);

        var snap = OrderSnapshotV1.From(order);

        snap.ServiceWindow.Should().BeNull();
    }

    [Fact]
    public void From_IsJsonRoundTrippable()
    {
        var order = BuildRichOrder();
        var snap = OrderSnapshotV1.From(order);

        var json = System.Text.Json.JsonSerializer.Serialize(snap);
        var restored = System.Text.Json.JsonSerializer.Deserialize<OrderSnapshotV1>(json);

        restored.Should().NotBeNull();
        restored!.Version.Should().Be("1.0");
        restored.Items.Should().HaveCount(1);
        restored.Items[0].ItemId.Should().Be("VACCINE-LOT-A-001");
        restored.Items[0].Hazmat!.ClassCode.Should().Be("6.2");
        restored.Items[0].HandlingInstructions.Should().BeEquivalentTo(new[] { "Fragile", "ThisSideUp" });
    }

    [Fact]
    public void OrderAmendment_NewRow_DefaultsAmendmentVersion_To_1()
    {
        var amendment = new DomainEntities.OrderAmendment(
            Guid.NewGuid(),
            DomainEntities.AmendmentType.ServiceWindowChange,
            reason: "test",
            originalSnapshot: "{}",
            newSnapshot: "{}");

        amendment.AmendmentVersion.Should().Be(1);
    }
}
