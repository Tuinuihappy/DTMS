using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using DomainEntities = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

namespace DeliveryOrder.UnitTests;

public class OrderSnapshotV1Tests
{
    private static DomainEntities.DeliveryOrder BuildRichOrder()
    {
        var order = DomainEntities.DeliveryOrder.Create(
            "SNAP-001", Priority.High,
            serviceWindow: ServiceWindow.Create(
                earliest: new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
                latest:   new DateTime(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc)),
            sourceSystem: SourceSystem.Manual,
            createdBy: "ops-user",
            slaTier: SlaTier.Gold);

        order.AddItem(
            "WH-COLD-01", "LAB-FREEZER",
            itemSeq: 1, sku: "VACCINE",
            description: "Refrigerated batch",
            loadUnitProfileCode: "TRAY-A",
            dimensions: Dimensions.Create(300, 200, 100),
            weightKg: 2.5,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            cargoType: CargoType.FinishedGood,
            cargoSpecific: CargoSpecific.Create(
                partNo: "VX-001", wo: null, line: null, vendor: "Pfizer",
                dateCode: "2026-05", tradingCode: null, inventoryNo: null,
                po: null, traceId: null, lotNo: "LOT-A"),
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
        snap.SlaTier.Should().Be("Gold");
        snap.OrderStatus.Should().Be("Draft");
        snap.ServiceWindow.Should().NotBeNull();
        snap.ServiceWindow!.Earliest.Should().Be(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        snap.ServiceWindow.Latest.Should().Be(new DateTime(2026, 6, 1, 17, 0, 0, DateTimeKind.Utc));
        snap.Items.Should().HaveCount(1);
    }

    [Fact]
    public void From_CapturesAllItemValueObjects()
    {
        var order = BuildRichOrder();

        var snap = OrderSnapshotV1.From(order);
        var item = snap.Items.Single();

        item.Sku.Should().Be("VACCINE");
        item.Quantity.Uom.Should().Be("BOX");
        item.Hazmat.Should().NotBeNull();
        item.Hazmat!.ClassCode.Should().Be("6.2");
        item.Hazmat.PackingGroup.Should().Be("II");
        item.Temperature.Should().NotBeNull();
        item.Temperature!.MinC.Should().Be(2);
        item.Temperature.MaxC.Should().Be(8);
        item.HandlingInstructions.Should().BeEquivalentTo(new[] { "Fragile", "ThisSideUp" });
        item.CargoSpecific.Should().NotBeNull();
        item.CargoSpecific!.LotNo.Should().Be("LOT-A");
        item.CargoType.Should().Be("FinishedGood");
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
