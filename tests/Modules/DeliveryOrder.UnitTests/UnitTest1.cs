using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Events;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

public class DeliveryOrderTests
{
    private static AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder CreateOrder(
        string orderRef = "Test Order") =>
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            orderRef, Priority.Normal, serviceWindow: null);

    private static void AddTestItem(
        AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder order,
        int itemSeq, string pickup, string drop, string sku,
        double? weightKg = 10.0, double quantity = 5, UnitOfMeasure uom = UnitOfMeasure.EA) =>
        order.AddItem(
            pickup, drop,
            itemSeq, sku,
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: weightKg, quantity: Quantity.Create(quantity, uom),
            cargoType: null, cargoSpecific: null);

    private static IReadOnlyDictionary<string, Guid> StationMap(params string[] codes)
    {
        var dict = new Dictionary<string, Guid>();
        foreach (var c in codes)
            dict[c] = Guid.NewGuid();
        return dict;
    }

    [Fact]
    public void NewOrder_StartsAsDraft()
    {
        var order = CreateOrder();

        order.Status.Should().Be(OrderStatus.Draft);
        order.Items.Should().BeEmpty();
    }

    [Fact]
    public void AddItem_AddsItemToOrder()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.Items.Should().HaveCount(1);
        order.Items.First().Sku.Should().Be("SKU-001");
        order.Items.First().PickupLocationCode.Should().Be("WH-01");
        order.Items.First().DropLocationCode.Should().Be("STORE-05");
        order.Items.First().ItemSeq.Should().Be(1);
    }

    [Fact]
    public void AddItem_TrimsLocationCodes()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "  WH-01 ", "STORE-05", "SKU-001");

        order.Items.First().PickupLocationCode.Should().Be("WH-01");
    }

    [Fact]
    public void AddItem_RejectsEmptyLocationCode()
    {
        var order = CreateOrder();

        var act = () => AddTestItem(order, itemSeq: 1, "   ", "STORE-05", "SKU-001");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddItem_DuplicateSeq_Throws()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        var act = () => AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-002");

        act.Should().Throw<InvalidOperationException>().WithMessage("*seq*1*");
    }

    [Fact]
    public void AddItem_SameSku_DifferentSeq_IsAllowed()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        var act = () => AddTestItem(order, itemSeq: 2, "WH-01", "STORE-05", "SKU-001");

        act.Should().NotThrow();
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var order = CreateOrder();

        order.Cancel("No longer needed");

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = CreateOrder();
        order.Cancel("First cancel");

        order.Cancel("Second cancel");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.DomainEvents.OfType<DeliveryOrderCancelledDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Submit_WhenDraft_SetsStatusToSubmitted()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.Submit();

        order.Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public void MarkAsValidated_SetsStationIdsOnItems()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();

        var pickupId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        order.MarkAsValidated(new Dictionary<string, Guid>
        {
            ["WH-01"] = pickupId,
            ["STORE-05"] = dropId,
        });

        order.Status.Should().Be(OrderStatus.Validated);
        order.Items.First().PickupStationId.Should().Be(pickupId);
        order.Items.First().DropStationId.Should().Be(dropId);
    }

    [Fact]
    public void MarkAsValidated_WhenNotSubmitted_Throws()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        var act = () => order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Confirm_FromValidated_SetsStatusToConfirmed()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        order.Confirm(weightFallbackKg: 500);

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Hold_SetsStatusToHeld()
    {
        var order = CreateOrder();

        order.Hold("waiting for space");

        order.Status.Should().Be(OrderStatus.Held);
        order.DomainEvents.OfType<DeliveryOrderHeldDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Release_FromHeld_SetsStatusToConfirmed()
    {
        var order = CreateOrder();
        order.Hold("waiting");

        order.Release(weightFallbackKg: 500);

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.DomainEvents.OfType<DeliveryOrderReleasedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Reject_FromSubmitted_SetsStatusToRejected()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();

        order.Reject("invalid station");

        order.Status.Should().Be(OrderStatus.Rejected);
        order.DomainEvents.OfType<DeliveryOrderRejectedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void MarkItemsDelivered_UpdatesMatchingItems()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        AddTestItem(order, itemSeq: 2, "WH-01", "STORE-05", "SKU-002", weightKg: 5.0, quantity: 2);

        order.MarkItemsDelivered(["SKU-001"]);

        order.Items.Single(i => i.Sku == "SKU-001").Status.Should().Be(ItemStatus.Delivered);
        order.Items.Single(i => i.Sku == "SKU-002").Status.Should().Be(ItemStatus.Pending);
    }

    [Fact]
    public void MarkItemsDelivered_IsCaseInsensitive()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.MarkItemsDelivered(["sku-001"]);

        order.Items.Single().Status.Should().Be(ItemStatus.Delivered);
    }

    [Fact]
    public void UpdateDraft_ReplacesCoreFieldsAndItems()
    {
        var order = CreateOrder("Original Ref");
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-OLD", weightKg: 5.0, quantity: 10, uom: UnitOfMeasure.EA);

        order.UpdateDraft("New Ref", Priority.High, serviceWindow: null);
        AddTestItem(order, itemSeq: 1, "WH-02", "LINE-02", "SKU-NEW", weightKg: 3.0, quantity: 5, uom: UnitOfMeasure.BOX);

        order.OrderRef.Should().Be("New Ref");
        order.Priority.Should().Be(Priority.High);
        order.Items.Should().HaveCount(1);
        order.Items.Single().Sku.Should().Be("SKU-NEW");
        order.TotalWeightKg.Should().Be(3.0);
        order.TotalQuantity.Should().Be(5);
        order.TotalItems.Should().Be(1);
    }

    [Fact]
    public void UpdateDraft_RaisesDeliveryOrderDraftUpdatedDomainEvent()
    {
        var order = CreateOrder();

        order.UpdateDraft(order.OrderRef, order.Priority, serviceWindow: null);

        order.DomainEvents.OfType<DeliveryOrderDraftUpdatedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void UpdateDraft_WhenNotDraft_Throws()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-001", weightKg: 5.0, quantity: 10, uom: UnitOfMeasure.EA);
        order.Submit();

        var act = () => order.UpdateDraft("New Ref", Priority.Low, serviceWindow: null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Draft*");
    }

    [Fact]
    public void UpdateDraft_ClearsTotals()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-A", weightKg: 10.0, quantity: 20, uom: UnitOfMeasure.EA);
        AddTestItem(order, itemSeq: 2, "WH-01", "LINE-02", "SKU-B", weightKg: 5.0, quantity: 10, uom: UnitOfMeasure.EA);

        order.UpdateDraft(order.OrderRef, order.Priority, serviceWindow: null);

        order.Items.Should().BeEmpty();
        order.TotalWeightKg.Should().Be(0);
        order.TotalQuantity.Should().Be(0);
        order.TotalItems.Should().Be(0);
    }

    [Fact]
    public void UpdateDraft_AllowsReAddingItemWithSameSeq()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "LINE-01", "SKU-REUSE", weightKg: 5.0, quantity: 10, uom: UnitOfMeasure.EA);

        order.UpdateDraft(order.OrderRef, order.Priority, serviceWindow: null);
        var act = () => AddTestItem(order, itemSeq: 1, "WH-02", "LINE-02", "SKU-REUSE", weightKg: 3.0, quantity: 5, uom: UnitOfMeasure.BOX);

        act.Should().NotThrow();
    }

    [Fact]
    public void AmendServiceWindow_UpdatesFieldAndPreservesStatus()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        var newTime = DateTime.UtcNow.AddHours(4);

        order.AmendServiceWindow(ServiceWindow.Create(earliest: null, latest: newTime), "rescheduled");

        order.ServiceWindow.Should().NotBeNull();
        order.ServiceWindow!.Latest.Should().Be(newTime);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.DomainEvents.OfType<DeliveryOrderAmendedDomainEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void AmendServiceWindow_WhenDraft_Throws()
    {
        var order = CreateOrder();

        var act = () => order.AmendServiceWindow(
            ServiceWindow.Create(earliest: null, latest: DateTime.UtcNow.AddHours(1)), "reason");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Draft*");
    }

    // ── P1-2: SlaTier + SubmittedAt (SLA clock) ─────────────────────────

    [Fact]
    public void NewOrder_DefaultsToBronzeTier_AndHasNoSubmittedAt()
    {
        var order = CreateOrder();

        order.SlaTier.Should().Be(SlaTier.Bronze);
        order.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public void Create_WithExplicitSlaTier_PreservesIt()
    {
        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            "TIER-TEST", Priority.Normal, serviceWindow: null,
            sourceSystem: SourceSystem.Manual, createdBy: null, slaTier: SlaTier.Gold);

        order.SlaTier.Should().Be(SlaTier.Gold);
    }

    [Fact]
    public void Submit_StartsSlaClock()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        var before = DateTime.UtcNow;

        order.Submit();

        order.SubmittedAt.Should().NotBeNull();
        order.SubmittedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void CreateFromUpstream_StartsSlaClockImmediately()
    {
        var before = DateTime.UtcNow;

        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.CreateFromUpstream(
            "UPS-001", Priority.High, ServiceWindow.Create(earliest: null, latest: DateTime.UtcNow.AddHours(2)),
            SourceSystem.Sap, createdBy: "sap-user", slaTier: SlaTier.Silver);

        order.SlaTier.Should().Be(SlaTier.Silver);
        order.Status.Should().Be(OrderStatus.Submitted);
        order.SubmittedAt.Should().NotBeNull();
        order.SubmittedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void SubmittedAt_PersistsThroughValidatedAndConfirmedTransitions()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        var clockStart = order.SubmittedAt;
        clockStart.Should().NotBeNull();

        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));
        order.SubmittedAt.Should().Be(clockStart);

        order.Confirm(weightFallbackKg: 500);
        order.SubmittedAt.Should().Be(clockStart);
    }

    [Fact]
    public void UpdateDraft_CanChangeSlaTier()
    {
        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            "UPD-001", Priority.Normal, serviceWindow: null,
            sourceSystem: SourceSystem.Manual, createdBy: null, slaTier: SlaTier.Bronze);

        order.UpdateDraft("UPD-001", Priority.High, serviceWindow: null, slaTier: SlaTier.Gold);

        order.SlaTier.Should().Be(SlaTier.Gold);
        order.Priority.Should().Be(Priority.High);
    }

    [Fact]
    public void Confirm_DomainEventCarriesSlaTierAndSubmittedAt()
    {
        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            "EVT-001", Priority.High, serviceWindow: ServiceWindow.Create(earliest: null, latest: DateTime.UtcNow.AddHours(4)),
            sourceSystem: SourceSystem.Manual, createdBy: null, slaTier: SlaTier.Gold);
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        confirmed.SlaTier.Should().Be("Gold");
        confirmed.SubmittedAt.Should().Be(order.SubmittedAt);
    }

    // ── P1-1: ServiceWindow VO + window-aware Confirm event ─────────────

    [Fact]
    public void ServiceWindow_Create_RejectsBothBoundsNull()
    {
        var act = () => ServiceWindow.Create(earliest: null, latest: null);

        act.Should().Throw<ArgumentException>().WithMessage("*at least one bound*");
    }

    [Fact]
    public void ServiceWindow_Create_RejectsEarliestAfterLatest()
    {
        var now = DateTime.UtcNow;
        var act = () => ServiceWindow.Create(earliest: now.AddHours(2), latest: now.AddHours(1));

        act.Should().Throw<ArgumentException>().WithMessage("*Earliest*on or before*Latest*");
    }

    [Fact]
    public void ServiceWindow_Create_AllowsEarliestOnly()
    {
        var earliest = DateTime.UtcNow.AddHours(1);

        var window = ServiceWindow.Create(earliest: earliest, latest: null);

        window.Earliest.Should().Be(earliest);
        window.Latest.Should().BeNull();
    }

    [Fact]
    public void ServiceWindow_Create_AllowsLatestOnly()
    {
        var latest = DateTime.UtcNow.AddHours(3);

        var window = ServiceWindow.Create(earliest: null, latest: latest);

        window.Earliest.Should().BeNull();
        window.Latest.Should().Be(latest);
    }

    [Fact]
    public void ServiceWindow_Equality_BasedOnBothBounds()
    {
        var early = DateTime.UtcNow.AddHours(1);
        var late = DateTime.UtcNow.AddHours(5);

        ServiceWindow.Create(early, late).Should().Be(ServiceWindow.Create(early, late));
        ServiceWindow.Create(null, late).Should().NotBe(ServiceWindow.Create(early, late));
    }

    [Fact]
    public void Confirm_DomainEvent_CarriesBothServiceWindowBounds()
    {
        var earliest = DateTime.UtcNow.AddHours(2);
        var latest = DateTime.UtcNow.AddHours(8);
        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            "WIN-001", Priority.Normal,
            serviceWindow: ServiceWindow.Create(earliest, latest),
            sourceSystem: SourceSystem.Manual, createdBy: null);
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        confirmed.Earliest.Should().Be(earliest);
        confirmed.Latest.Should().Be(latest);
    }

    [Fact]
    public void Confirm_DomainEvent_DeadlineMirrorsLatest_ForBackwardCompat()
    {
        var earliest = DateTime.UtcNow.AddHours(2);
        var latest = DateTime.UtcNow.AddHours(8);
        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            "DEADLINE-COMPAT", Priority.Normal,
            serviceWindow: ServiceWindow.Create(earliest, latest),
            sourceSystem: SourceSystem.Manual, createdBy: null);
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        confirmed.Deadline.Should().Be(latest, "Deadline is a v1-compat alias for Latest");
    }

    [Fact]
    public void Confirm_DomainEvent_DeadlineIsNull_WhenNoServiceWindow()
    {
        var order = AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities.DeliveryOrder.Create(
            "NO-WINDOW", Priority.Normal, serviceWindow: null,
            sourceSystem: SourceSystem.Manual, createdBy: null);
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));

        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        confirmed.Earliest.Should().BeNull();
        confirmed.Latest.Should().BeNull();
        confirmed.Deadline.Should().BeNull();
    }

    // ── P1-9: Quantity VO + UnitOfMeasure enum ──────────────────────────

    [Fact]
    public void Quantity_Create_RejectsZeroOrNegativeValue()
    {
        var actZero = () => Quantity.Create(0, UnitOfMeasure.EA);
        var actNeg  = () => Quantity.Create(-5, UnitOfMeasure.KG);

        actZero.Should().Throw<ArgumentException>().WithMessage("*greater than zero*");
        actNeg.Should().Throw<ArgumentException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Quantity_Equality_IsStructural()
    {
        var a = Quantity.Create(10, UnitOfMeasure.EA);
        var b = Quantity.Create(10, UnitOfMeasure.EA);
        var c = Quantity.Create(10, UnitOfMeasure.BOX);
        var d = Quantity.Create(5, UnitOfMeasure.EA);

        a.Should().Be(b);
        a.Should().NotBe(c);
        a.Should().NotBe(d);
    }

    // ── P1-3: HazmatInfo VO ────────────────────────────────────────────

    [Theory]
    [InlineData("3")]
    [InlineData("2.1")]
    [InlineData("5.1")]
    [InlineData("6.1")]
    [InlineData("9")]
    public void HazmatInfo_Create_AcceptsValidClassCodes(string classCode)
    {
        var act = () => HazmatInfo.Create(classCode, PackingGroup.II);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("3.0")]
    [InlineData("2.7")]
    [InlineData("X")]
    [InlineData("3a")]
    public void HazmatInfo_Create_RejectsInvalidClassCodes(string classCode)
    {
        var act = () => HazmatInfo.Create(classCode, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HazmatInfo_PackingGroup_IsOptional_AndPreserved()
    {
        var withPg = HazmatInfo.Create("3", PackingGroup.II);
        var withoutPg = HazmatInfo.Create("7", null);

        withPg.PackingGroup.Should().Be(PackingGroup.II);
        withoutPg.PackingGroup.Should().BeNull();
    }

    [Fact]
    public void HazmatInfo_TrimsWhitespaceInClassCode()
    {
        var hz = HazmatInfo.Create("  3  ", PackingGroup.I);

        hz.ClassCode.Should().Be("3");
    }

    [Fact]
    public void Item_DefaultsToNonHazardous_AndHazmatCanBeAttached()
    {
        var order = CreateOrder();

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, sku: "PAPER",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(10, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null);

        order.AddItem(
            "WH-FLAM-01", "LINE-02",
            itemSeq: 2, sku: "THINNER",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 25.0,
            quantity: Quantity.Create(2, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: HazmatInfo.Create("3", PackingGroup.II));

        var paper = order.Items.Single(i => i.Sku == "PAPER");
        var thinner = order.Items.Single(i => i.Sku == "THINNER");

        paper.Hazmat.Should().BeNull();
        thinner.Hazmat.Should().NotBeNull();
        thinner.Hazmat!.ClassCode.Should().Be("3");
        thinner.Hazmat.PackingGroup.Should().Be(PackingGroup.II);
    }

    [Fact]
    public void Confirm_DomainEvent_CarriesHazmatPerItem()
    {
        var order = CreateOrder();
        order.AddItem(
            "WH-01", "STORE-05",
            itemSeq: 1, sku: "SKU-CLEAN",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null);
        order.AddItem(
            "WH-FLAM-01", "STORE-05",
            itemSeq: 2, sku: "SKU-ACID",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 10.0,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: HazmatInfo.Create("8", PackingGroup.II));
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "WH-FLAM-01", "STORE-05"));
        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        var clean = confirmed.Items.Single(i => i.Sku == "SKU-CLEAN");
        var acid = confirmed.Items.Single(i => i.Sku == "SKU-ACID");

        clean.Hazmat.Should().BeNull();
        acid.Hazmat.Should().NotBeNull();
        acid.Hazmat!.ClassCode.Should().Be("8");
        acid.Hazmat.PackingGroup.Should().Be("II");
    }

    // ── P1-4: TemperatureRange VO ─────────────────────────────────────

    [Fact]
    public void TemperatureRange_Create_RejectsBothBoundsNull()
    {
        var act = () => TemperatureRange.Create(null, null);
        act.Should().Throw<ArgumentException>().WithMessage("*at least one bound*");
    }

    [Fact]
    public void TemperatureRange_Create_RejectsMinAboveMax()
    {
        var act = () => TemperatureRange.Create(minC: 10, maxC: 5);
        act.Should().Throw<ArgumentException>().WithMessage("*MinC must be on or below MaxC*");
    }

    [Theory]
    [InlineData(2.0, 8.0)]      // refrigerated pharma
    [InlineData(-20.0, -18.0)]  // frozen
    [InlineData(-196.0, null)]  // cryogenic, no upper bound
    [InlineData(null, 25.0)]    // ambient ceiling (e.g. chocolate)
    [InlineData(60.0, 60.0)]    // single-temp clamp (e.g. exact storage)
    public void TemperatureRange_Create_AcceptsValidBounds(double? minC, double? maxC)
    {
        var act = () => TemperatureRange.Create(minC, maxC);
        act.Should().NotThrow();
    }

    [Fact]
    public void TemperatureRange_Equality_BasedOnBothBounds()
    {
        TemperatureRange.Create(2, 8).Should().Be(TemperatureRange.Create(2, 8));
        TemperatureRange.Create(2, 8).Should().NotBe(TemperatureRange.Create(2, 10));
        TemperatureRange.Create(null, 8).Should().NotBe(TemperatureRange.Create(2, 8));
    }

    [Fact]
    public void Item_DefaultsToAmbient_AndTemperatureCanBeAttached()
    {
        var order = CreateOrder();

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, sku: "PAPER",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(10, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null);

        order.AddItem(
            "WH-COLD-01", "LAB-FREEZER",
            itemSeq: 2, sku: "VACCINE",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 2.0,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: null,
            temperature: TemperatureRange.Create(2.0, 8.0));

        var paper = order.Items.Single(i => i.Sku == "PAPER");
        var vaccine = order.Items.Single(i => i.Sku == "VACCINE");

        paper.Temperature.Should().BeNull();
        vaccine.Temperature.Should().NotBeNull();
        vaccine.Temperature!.MinC.Should().Be(2.0);
        vaccine.Temperature.MaxC.Should().Be(8.0);
    }

    [Fact]
    public void Confirm_DomainEvent_CarriesTemperaturePerItem()
    {
        var order = CreateOrder();
        order.AddItem(
            "WH-01", "STORE-05",
            itemSeq: 1, sku: "SKU-AMBIENT",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null);
        order.AddItem(
            "WH-COLD-01", "STORE-05",
            itemSeq: 2, sku: "SKU-COLD",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 3.0,
            quantity: Quantity.Create(1, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: null,
            temperature: TemperatureRange.Create(minC: null, maxC: 8.0));
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "WH-COLD-01", "STORE-05"));
        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        var ambient = confirmed.Items.Single(i => i.Sku == "SKU-AMBIENT");
        var cold = confirmed.Items.Single(i => i.Sku == "SKU-COLD");

        ambient.Temperature.Should().BeNull();
        cold.Temperature.Should().NotBeNull();
        cold.Temperature!.MinC.Should().BeNull();
        cold.Temperature.MaxC.Should().Be(8.0);
    }

    // ── P1-5: HandlingInstructions ────────────────────────────────────

    [Fact]
    public void Item_HandlingInstructions_DefaultsToEmpty()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-001");

        order.Items.Single().HandlingInstructions.Should().BeEmpty();
    }

    [Fact]
    public void AddItem_AttachesMultipleHandlingInstructions()
    {
        var order = CreateOrder();
        var instructions = new[] { HandlingInstruction.Fragile, HandlingInstruction.ThisSideUp };

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, sku: "GLASS",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(10, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: null, temperature: null,
            handlingInstructions: instructions);

        var item = order.Items.Single();
        item.HandlingInstructions.Should().BeEquivalentTo(instructions);
    }

    [Fact]
    public void AddItem_DedupesDuplicateHandlingInstructions()
    {
        var order = CreateOrder();
        var withDupes = new[]
        {
            HandlingInstruction.Fragile,
            HandlingInstruction.Fragile,
            HandlingInstruction.ThisSideUp
        };

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, sku: "GLASS",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(10, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: null, temperature: null,
            handlingInstructions: withDupes);

        var item = order.Items.Single();
        item.HandlingInstructions.Should().HaveCount(2);
        item.HandlingInstructions.Should().BeEquivalentTo(
            new[] { HandlingInstruction.Fragile, HandlingInstruction.ThisSideUp });
    }

    [Fact]
    public void AddItem_NullHandlingInstructions_StoredAsEmpty()
    {
        var order = CreateOrder();

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, sku: "PLAIN",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(10, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: null, temperature: null,
            handlingInstructions: null);

        order.Items.Single().HandlingInstructions.Should().BeEmpty();
    }

    [Fact]
    public void Confirm_DomainEvent_CarriesHandlingInstructionsPerItem()
    {
        var order = CreateOrder();
        AddTestItem(order, itemSeq: 1, "WH-01", "STORE-05", "SKU-PLAIN");
        order.AddItem(
            "WH-01", "STORE-05",
            itemSeq: 2, sku: "SKU-GLASS",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(10, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null,
            hazmat: null, temperature: null,
            handlingInstructions: new[]
            {
                HandlingInstruction.Fragile,
                HandlingInstruction.ThisSideUp
            });
        order.Submit();
        order.MarkAsValidated(StationMap("WH-01", "STORE-05"));
        order.Confirm(weightFallbackKg: 500);

        var confirmed = order.DomainEvents.OfType<DeliveryOrderConfirmedDomainEvent>().Single();
        var plain = confirmed.Items.Single(i => i.Sku == "SKU-PLAIN");
        var glass = confirmed.Items.Single(i => i.Sku == "SKU-GLASS");

        plain.HandlingInstructions.Should().BeNull();   // empty list serialized as null on the event
        glass.HandlingInstructions.Should().NotBeNull();
        glass.HandlingInstructions!.Should().BeEquivalentTo(new[] { "Fragile", "ThisSideUp" });
    }

    [Fact]
    public void AddItem_StoresQuantityVO_WithCanonicalUom()
    {
        var order = CreateOrder();

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, sku: "SKU-Q",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(12, UnitOfMeasure.BOX),
            cargoType: null, cargoSpecific: null);

        var item = order.Items.Single();
        item.Quantity.Value.Should().Be(12);
        item.Quantity.Uom.Should().Be(UnitOfMeasure.BOX);
        // TotalQuantity continues to aggregate by raw value (Uom-mixed for now;
        // capacity-aware aggregation by Uom is Planning-solver territory).
        order.TotalQuantity.Should().Be(12);
    }
}
