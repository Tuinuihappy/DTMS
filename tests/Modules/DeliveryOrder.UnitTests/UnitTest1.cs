using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

public class DeliveryOrderTests
{
    private static AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder CreateOrder() =>
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            Guid.NewGuid(), "Test Order", SlaTier.Normal,
            new ServiceWindow(null, null));

    private static void AddPackage(
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder order,
        string pickup = "LOC-A",
        string drop = "LOC-B",
        string carrierTypeCode = "FEEDER",
        string barcode = "BOX-001",
        string profileCode = "TRAY-SMALL",
        double grossWeightKg = 2.5,
        IEnumerable<(string itemNumber, double quantity)>? contents = null)
        => order.AddPackage(pickup, drop, carrierTypeCode, barcode, profileCode, grossWeightKg, contents);

    private static IReadOnlyDictionary<(string, string), (Guid, Guid)> StationMap(
        params (string pickup, string drop)[] routes)
    {
        var dict = new Dictionary<(string, string), (Guid, Guid)>();
        foreach (var r in routes)
            dict[r] = (Guid.NewGuid(), Guid.NewGuid());
        return dict;
    }

    // ── Core behaviour ────────────────────────────────────────────────────────

    [Fact]
    public void NewOrder_StartsAsDraft()
    {
        var order = CreateOrder();

        order.Status.Should().Be(OrderStatus.Draft);
        order.Legs.Should().BeEmpty();
    }

    [Fact]
    public void AddPackage_CreatesLeg()
    {
        var order = CreateOrder();
        AddPackage(order);

        order.Legs.Should().HaveCount(1);
        order.Legs.First().PickupLocationCode.Should().Be("LOC-A");
        order.Legs.First().DropLocationCode.Should().Be("LOC-B");
        order.Legs.First().CarrierTypeCode.Should().Be("FEEDER");
        order.AllPackages.Should().HaveCount(1);
    }

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var order = CreateOrder();

        order.Cancel("No longer needed");

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void MarkAsValidated_SetsStationIdsOnLegs()
    {
        var order = CreateOrder();
        AddPackage(order);
        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        order.Submit();
        order.MarkAsValidated(new Dictionary<(string, string), (Guid, Guid)>
        {
            [("LOC-A", "LOC-B")] = (pickupId, dropId)
        });

        order.Status.Should().Be(OrderStatus.Validated);
        order.Legs.First().PickupStationId.Should().Be(pickupId);
        order.Legs.First().DropStationId.Should().Be(dropId);
    }

    [Fact]
    public void MarkAsValidated_WhenNotSubmitted_Throws()
    {
        var order = CreateOrder();
        AddPackage(order);

        var act = () => order.MarkAsValidated(StationMap(("LOC-A", "LOC-B")));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetRecurringSchedule_SetsSchedule()
    {
        var order = CreateOrder();

        order.SetRecurringSchedule("0 */2 * * *", DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        order.Schedule.Should().NotBeNull();
        order.Schedule!.CronExpression.Should().Be("0 */2 * * *");
    }

    // ── CarrierTypeCode leg grouping ──────────────────────────────────────────

    [Fact]
    public void AddPackage_SameRouteAndCarrier_MergesIntoOneLeg()
    {
        var order = CreateOrder();

        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-001");
        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-002");

        order.Legs.Should().HaveCount(1);
        order.Legs.First().Packages.Should().HaveCount(2);
    }

    [Fact]
    public void AddPackage_SameRouteButDifferentCarrier_CreatesTwoLegs()
    {
        var order = CreateOrder();

        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-001");
        AddPackage(order, "LOC-A", "LOC-B", "SHELF",  "BOX-001");

        order.Legs.Should().HaveCount(2);
        order.Legs.Select(l => l.CarrierTypeCode).Should()
            .BeEquivalentTo(new[] { "FEEDER", "SHELF" });
    }

    [Fact]
    public void AddPackage_DifferentRoutes_CreatesTwoLegs()
    {
        var order = CreateOrder();

        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-001");
        AddPackage(order, "LOC-C", "LOC-D", "FEEDER", "TRAY-002");

        order.Legs.Should().HaveCount(2);
    }

    [Fact]
    public void AddPackage_SequenceIsUniquePerNewLeg()
    {
        var order = CreateOrder();

        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-001");
        AddPackage(order, "LOC-A", "LOC-B", "SHELF",  "BOX-001");
        AddPackage(order, "LOC-C", "LOC-D", "FEEDER", "TRAY-002");

        order.Legs.Select(l => l.Sequence).Should().OnlyHaveUniqueItems();
        order.Legs.Select(l => l.Sequence).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    // ── MarkReadyToPlan domain event ──────────────────────────────────────────

    [Fact]
    public void MarkReadyToPlan_LegEventHasCorrectCarrierTypeAndPackageCount()
    {
        var order = CreateOrder();
        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-001", grossWeightKg: 3.0);
        AddPackage(order, "LOC-A", "LOC-B", "FEEDER", "TRAY-002", grossWeightKg: 4.0);
        AddPackage(order, "LOC-A", "LOC-B", "SHELF",  "BOX-001",  grossWeightKg: 20.0);
        order.Submit();
        order.MarkAsValidated(StationMap(("LOC-A", "LOC-B")));
        order.MarkReadyToPlan();

        var evt = order.DomainEvents
            .OfType<DeliveryOrderReadyToPlanDomainEvent>()
            .Single();

        evt.Legs.Should().HaveCount(2);

        var feederLeg = evt.Legs.Single(l => l.CarrierTypeCode == "FEEDER");
        feederLeg.TotalPackageCount.Should().Be(2);
        feederLeg.TotalWeight.Should().Be(7.0);

        var shelfLeg = evt.Legs.Single(l => l.CarrierTypeCode == "SHELF");
        shelfLeg.TotalPackageCount.Should().Be(1);
        shelfLeg.TotalWeight.Should().Be(20.0);
    }

    // ── PackageUnit contents ───────────────────────────────────────────────────

    [Fact]
    public void AddPackage_WithContents_StoresContentItems()
    {
        var order = CreateOrder();
        order.AddPackage("LOC-A", "LOC-B", "FEEDER", "BOX-001", "CARTON-A3", 5.0,
            contents: [("MOTOR-A", 5), ("PCB-B", 3)]);

        var pkg = order.AllPackages.Single();
        pkg.Contents.Should().HaveCount(2);
        pkg.Contents.Should().Contain(c => c.ItemNumber == "MOTOR-A" && c.Quantity == 5);
        pkg.Contents.Should().Contain(c => c.ItemNumber == "PCB-B"   && c.Quantity == 3);
    }

    // ── MarkPackagesDelivered ─────────────────────────────────────────────────

    [Fact]
    public void MarkPackagesDelivered_UpdatesMatchingBarcodes()
    {
        var order = CreateOrder();
        AddPackage(order, barcode: "BOX-001");
        AddPackage(order, barcode: "BOX-002");

        order.MarkPackagesDelivered(["BOX-001"]);

        order.AllPackages.Single(p => p.Barcode == "BOX-001").Status.Should().Be(PackageStatus.Delivered);
        order.AllPackages.Single(p => p.Barcode == "BOX-002").Status.Should().Be(PackageStatus.Pending);
    }

    [Fact]
    public void MarkPackagesDelivered_IsCaseInsensitive()
    {
        var order = CreateOrder();
        AddPackage(order, barcode: "BOX-001");

        order.MarkPackagesDelivered(["box-001"]);

        order.AllPackages.Single().Status.Should().Be(PackageStatus.Delivered);
    }
}
