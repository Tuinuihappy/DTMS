using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;
using DomainEntities = DTMS.DeliveryOrder.Domain.Entities;

namespace DeliveryOrder.UnitTests;

/// <summary>
/// Uom is free-form text since the UomNormalizer removal. These tests pin the
/// two halves of that decision: anything a site actually calls a unit goes
/// through untouched, and blank is still not a unit.
/// </summary>
public class FreeFormUomTests
{
    // ── The value object stores verbatim ────────────────────────────────

    [Theory]
    [InlineData("EA")]
    [InlineData("ea")]          // case is NOT folded — "ea" and "EA" are distinct
    [InlineData("mL")]          // mixed case survives
    [InlineData("SQ_M")]        // underscores survive
    [InlineData("ถุงใหญ่")]      // non-latin survives
    [InlineData("PCS")]         // a former alias is now just another unit
    [InlineData(" EA ")]        // NOT trimmed — the decision was to store raw
    public void Create_StoresUomVerbatim(string uom)
    {
        Quantity.Create(3, uom).Uom.Should().Be(uom);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankUom(string uom)
    {
        var act = () => Quantity.Create(3, uom);

        act.Should().Throw<ArgumentException>().WithParameterName("uom");
    }

    [Fact]
    public void AddItem_RoundTripsAFreeFormUom()
    {
        var order = DomainEntities.DeliveryOrder.Create(
            "UOM-001", Priority.Normal,
            ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)));

        order.AddItem(
            "WH-01", "LINE-01",
            itemSeq: 1, itemId: "SKU-Q",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 5.0,
            quantity: Quantity.Create(12, "ถุงใหญ่"));

        order.Items.Single().Quantity.Uom.Should().Be("ถุงใหญ่");
    }

    // ── Validators are what produce the 400 ─────────────────────────────

    [Theory]
    [InlineData("ถุงใหญ่")]
    [InlineData("mL")]
    [InlineData("PCS")]
    public void DraftValidator_AcceptsAnyNonBlankUom(string uom)
    {
        new DraftItemDtoValidator().Validate(Item(uom)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DraftValidator_RejectsBlankUom(string uom)
    {
        // Before the normalizer was removed, "" was rejected as an unknown
        // unit by the handler. DraftItemDtoValidator carried only a length
        // rule, so without NotEmpty a blank unit would have reached the DB.
        var result = new DraftItemDtoValidator().Validate(Item(uom));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Uom"));
    }

    [Fact]
    public void DraftValidator_RejectsUomOverTwentyChars()
    {
        new DraftItemDtoValidator().Validate(Item(new string('X', 21)))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void DraftValidator_AcceptsUomOfExactlyTwentyChars()
    {
        new DraftItemDtoValidator().Validate(Item(new string('X', 20)))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SubmitValidator_RejectsBlankUom(string uom)
    {
        new SubmitItemDtoValidator().Validate(Item(uom)).IsValid.Should().BeFalse();
    }

    // ── SubmitReadinessCheck mirrors SubmitItemDtoValidator ─────────────

    [Fact]
    public void SubmitReadiness_RejectsAnItemWhoseUomIsBlank()
    {
        // Quantity.Create guards blank uom, so build the entity through the
        // private setter path EF uses rather than the factory.
        var order = OrderWithItemUom("  ");

        var (isValid, error) = SubmitReadinessCheck.Check(order);

        isValid.Should().BeFalse();
        error.Should().Contain("Uom");
    }

    [Fact]
    public void SubmitReadiness_AcceptsAFreeFormUom()
    {
        SubmitReadinessCheck.Check(OrderWithItemUom("ถุงใหญ่")).IsValid.Should().BeTrue();
    }

    // ── Fixtures ────────────────────────────────────────────────────────

    private static ItemDto Item(string uom) => new(
        ItemId: "SKU-1",
        Description: null,
        PickupLocationCode: "LOC-A",
        DropLocationCode: "LOC-B",
        LoadUnitProfileCode: null,
        Dimensions: null,
        WeightKg: 1.0,
        Quantity: new QuantityDto(2, uom));

    private static DomainEntities.DeliveryOrder OrderWithItemUom(string uom)
    {
        var order = DomainEntities.DeliveryOrder.Create(
            "UOM-RDY", Priority.Normal,
            ServiceWindow.Create(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(4)));

        order.AddItem(
            "LOC-A", "LOC-B",
            itemSeq: 1, itemId: "SKU-1",
            description: null, loadUnitProfileCode: null,
            dimensions: null, weightKg: 1.0,
            quantity: Quantity.Create(2, "EA"));

        // Reach past the factory guard the way a legacy row materialised by EF
        // would — SubmitReadinessCheck exists to catch exactly that shape.
        var quantity = order.Items.Single().Quantity;
        typeof(Quantity).GetProperty(nameof(Quantity.Uom))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(quantity, [uom]);

        return order;
    }
}
