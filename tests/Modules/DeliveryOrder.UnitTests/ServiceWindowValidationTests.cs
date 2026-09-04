using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Domain.ValueObjects;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

// A reversed ServiceWindow is a caller typo, so both create paths must reject
// it as a 400 at validation time. The upstream path used to check only "at
// least one bound", letting a reversed window through to
// ServiceWindow.Create — whose ArgumentException matches neither the handler's
// catch clauses nor a modeled arm in the exception middleware, so the caller
// got a 500. These pin the rule on both validators so the two paths can't
// drift apart again.
public class ServiceWindowValidationTests
{
    private static readonly DateTime Earlier = new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 9, 3, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Upstream_ReversedWindow_FailsValidation()
    {
        var result = new CreateUpstreamDeliveryOrderCommandValidator()
            .Validate(UpstreamCommand(earliest: Later, latest: Earlier));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("EarliestUtc must be on or before LatestUtc"));
    }

    [Fact]
    public void Upstream_OrderedWindow_Passes()
    {
        var result = new CreateUpstreamDeliveryOrderCommandValidator()
            .Validate(UpstreamCommand(earliest: Earlier, latest: Later));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Upstream_SingleBound_StillPasses()
    {
        // One-sided windows stay legal — the new rule must not tighten that.
        var result = new CreateUpstreamDeliveryOrderCommandValidator()
            .Validate(UpstreamCommand(earliest: null, latest: Later));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Draft_ReversedWindow_FailsValidation()
    {
        var result = new CreateDraftDeliveryOrderCommandValidator()
            .Validate(new CreateDraftDeliveryOrderCommand(
                "OD-SW-01", new ServiceWindowDto(Later, Earlier), [Item()]));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void DomainFactory_RejectsReversedWindow()
    {
        // The invariant the validators are mirroring — kept here so a change
        // to one is visibly a change to the pair.
        var act = () => ServiceWindow.Create(Later, Earlier);

        act.Should().Throw<ArgumentException>();
    }

    private static CreateUpstreamDeliveryOrderCommand UpstreamCommand(
        DateTime? earliest, DateTime? latest) => new(
        "OD-SW-01",
        new ServiceWindowDto(earliest, latest),
        [Item()],
        SourceSystemKey: "oms");

    private static ItemDto Item() => new(
        ItemId: "SKU-1", Description: null,
        PickupLocationCode: "LOC-A", DropLocationCode: "LOC-B",
        LoadUnitProfileCode: null, Dimensions: null, WeightKg: 1.0,
        Quantity: new QuantityDto(1, "EA"));
}
