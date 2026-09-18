using DTMS.Fleet.Application.Commands.DeleteCarrierType;
using DTMS.Fleet.Application.Commands.UpdateCarrierType;
using DTMS.Fleet.Application.Queries.GetCarrierTypeByCode;
using DTMS.Fleet.Application.Queries.GetCarrierTypes;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Events;
using DTMS.Fleet.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Fleet.UnitTests;

/// <summary>
/// The catalogue half of ADR-019. A carrier type has no lifecycle, so what is
/// worth pinning down is narrower than <see cref="CarrierTests"/>: that the code
/// stays URL-safe and immutable, and that a type still in use cannot be deleted
/// out from under the carriers holding it.
/// </summary>
public class CarrierTypeTests
{
    private static CarrierType NewType(string code = "SHELF") =>
        new(code, "Shelf", "STANDARD", 200, 12, "Two-tier shelf");

    [Theory]
    [InlineData("shelf", "SHELF")]
    [InlineData("  eur-pallet  ", "EUR-PALLET")]
    public void Code_IsNormalizedToUpperInvariant(string input, string expected)
        => new CarrierType(input, "X", "STANDARD").Code.Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EUR PALLET")]   // space
    [InlineData("A/B")]          // path separator
    [InlineData("100%")]         // percent-encoding
    [InlineData("-LEADING")]     // must start with a letter or digit
    public void Code_OutsideTheUrlSafeCharset_IsRefused(string code)
    {
        // The code becomes a path segment on /carrier-types/{code}; a space or
        // slash breaks routing, and the person who finds out is an operator.
        var create = () => new CarrierType(code, "X", "STANDARD");
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_ChangesEveryMutableFieldAndLeavesTheCodeAlone()
    {
        var type = NewType();

        type.Update("Tall shelf", "LIFT", 350, 20, "  three-tier  ");

        type.Code.Should().Be("SHELF");
        type.DisplayName.Should().Be("Tall shelf");
        type.AmrCapability.Should().Be("LIFT");
        type.MaxWeightKg.Should().Be(350);
        type.MaxSlots.Should().Be(20);
        type.Description.Should().Be("three-tier");
    }

    [Fact]
    public void Update_ClearsAnEmptiedDescriptionRatherThanStoringBlankText()
    {
        var type = NewType();

        type.Update("Shelf", "STANDARD", null, null, "   ");

        type.Description.Should().BeNull();
        type.MaxWeightKg.Should().BeNull();
        type.MaxSlots.Should().BeNull();
    }

    [Theory]
    [InlineData("", "STANDARD")]
    [InlineData("   ", "STANDARD")]
    [InlineData("Shelf", "")]
    public void Update_WithAMissingRequiredField_IsRefused(string displayName, string amrCapability)
    {
        var type = NewType();
        var update = () => type.Update(displayName, amrCapability, null, null, null);
        update.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0d, null)]
    [InlineData(-1d, null)]
    [InlineData(null, 0)]
    [InlineData(null, -5)]
    public void Update_WithANonPositiveCapacity_IsRefused(double? maxWeightKg, int? maxSlots)
    {
        var type = NewType();
        var update = () => type.Update("Shelf", "STANDARD", maxWeightKg, maxSlots, null);
        update.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanDelete_IsRefusedWhileCarriersHoldTheType_AndSaysHowMany()
    {
        var (canDelete, reason) = NewType().CanDelete(carriersUsing: 3);

        canDelete.Should().BeFalse();
        reason.Should().Contain("3 carrier(s)");
    }

    [Fact]
    public void CanDelete_IsAllowedWhenNothingHoldsTheType()
        => NewType().CanDelete(0).CanDelete.Should().BeTrue();

    // ── handlers ────────────────────────────────────────────────────────────

    private readonly ICarrierTypeRepository _types = Substitute.For<ICarrierTypeRepository>();
    private readonly ICarrierRepository _carriers = Substitute.For<ICarrierRepository>();
    private readonly IAttachmentRepository _attachments = Substitute.For<IAttachmentRepository>();

    [Fact]
    public async Task Update_OfAMissingType_Fails()
    {
        _types.GetByCodeAsync("NOPE", Arg.Any<CancellationToken>()).Returns((CarrierType?)null);

        var result = await new UpdateCarrierTypeCommandHandler(_types)
            .Handle(new UpdateCarrierTypeCommand("NOPE", "X", "STANDARD"), default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("NOPE");
    }

    [Fact]
    public async Task Update_AppliesTheEditAndSavesOnce()
    {
        var type = NewType();
        _types.GetByCodeAsync("SHELF", Arg.Any<CancellationToken>()).Returns(type);

        var result = await new UpdateCarrierTypeCommandHandler(_types)
            .Handle(new UpdateCarrierTypeCommand("SHELF", "Tall shelf", "LIFT", 350, 20, null), default);

        result.IsSuccess.Should().BeTrue();
        type.DisplayName.Should().Be("Tall shelf");
        await _types.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_TurnsTheAggregatesRefusalIntoAFailure_NotAnException()
    {
        // Without the catch this would surface as a scrubbed 500 rather than a
        // 400 naming the field the caller left empty.
        var type = NewType();
        _types.GetByCodeAsync("SHELF", Arg.Any<CancellationToken>()).Returns(type);

        var result = await new UpdateCarrierTypeCommandHandler(_types)
            .Handle(new UpdateCarrierTypeCommand("SHELF", "", "STANDARD"), default);

        result.IsSuccess.Should().BeFalse();
        type.DisplayName.Should().Be("Shelf");
        await _types.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_OfAMissingType_IsNotFound()
    {
        _types.GetByCodeAsync("NOPE", Arg.Any<CancellationToken>()).Returns((CarrierType?)null);

        var result = await DeleteHandler().Handle(new DeleteCarrierTypeCommand("NOPE"), default);

        result.Value.Outcome.Should().Be(DeleteCarrierTypeOutcome.NotFound);
    }

    [Fact]
    public async Task Delete_WhileCarriersHoldTheType_IsBlockedBeforeTheForeignKeyCanRefuse()
    {
        var type = NewType();
        _types.GetByCodeAsync("SHELF", Arg.Any<CancellationToken>()).Returns(type);
        _carriers.CountForCarrierTypeAsync(type.Id, Arg.Any<CancellationToken>()).Returns(4);

        var result = await DeleteHandler().Handle(new DeleteCarrierTypeCommand("SHELF"), default);

        // Carriers.CarrierTypeId is ON DELETE RESTRICT: letting this through
        // would trade a message naming four carriers for a raw 23503.
        result.Value.Outcome.Should().Be(DeleteCarrierTypeOutcome.Blocked);
        result.Value.Reason.Should().Contain("4 carrier(s)");
        _types.DidNotReceive().Remove(Arg.Any<CarrierType>());
        await _attachments.DidNotReceive().ListForOwnerForDeleteAsync(
            Arg.Any<AttachmentOwner>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_TakesTheTypesPhotosWithIt()
    {
        var type = NewType();
        _types.GetByCodeAsync("SHELF", Arg.Any<CancellationToken>()).Returns(type);
        _carriers.CountForCarrierTypeAsync(type.Id, Arg.Any<CancellationToken>()).Returns(0);

        var photo = PhotoOf(type.Id);
        _attachments.ListForOwnerForDeleteAsync(
                        AttachmentOwner.CarrierType, type.Id, Arg.Any<CancellationToken>())
                    .Returns([photo]);

        var result = await DeleteHandler().Handle(new DeleteCarrierTypeCommand("SHELF"), default);

        result.Value.Outcome.Should().Be(DeleteCarrierTypeOutcome.Deleted);

        // The FK cascades, but it runs below EF — left to it the rows would go
        // and the bytes would stay in the bucket with nothing knowing their keys.
        photo.DomainEvents.OfType<AttachmentObjectsOrphanedDomainEvent>()
             .Should().ContainSingle()
             .Which.ObjectKeys.Should().BeEquivalentTo(photo.AllObjectKeys());

        _attachments.Received(1).Remove(photo);
        _types.Received(1).Remove(type);

        // One save, so the row, its images and the delete instructions commit
        // together or not at all.
        await _types.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _attachments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByCode_OfAMissingType_Fails()
    {
        _types.GetByCodeAsync("NOPE", Arg.Any<CancellationToken>()).Returns((CarrierType?)null);

        var result = await new GetCarrierTypeByCodeQueryHandler(_types, _attachments, _carriers)
            .Handle(new GetCarrierTypeByCodeQuery("NOPE"), default);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetByCode_CarriesTheSamePhotoSummaryTheListWouldShow()
    {
        var type = NewType();
        var cover = Guid.NewGuid();
        _types.GetByCodeAsync("SHELF", Arg.Any<CancellationToken>()).Returns(type);
        _attachments.GetSummariesForOwnersAsync(
                        AttachmentOwner.CarrierType,
                        Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(type.Id)),
                        Arg.Any<CancellationToken>())
                    .Returns(new Dictionary<Guid, AttachmentSummary>
                    {
                        [type.Id] = new(cover, 3)
                    });

        var result = await new GetCarrierTypeByCodeQueryHandler(_types, _attachments, _carriers)
            .Handle(new GetCarrierTypeByCodeQuery("SHELF"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.CoverAttachmentId.Should().Be(cover);
        result.Value.PhotoCount.Should().Be(3);
    }

    [Fact]
    public async Task List_CountsCarriersForEveryTypeInOneQuery()
    {
        var shelf = NewType("SHELF");
        var truck = NewType("TRUCK");
        _types.GetAllAsync(Arg.Any<CancellationToken>()).Returns([shelf, truck]);
        _attachments.GetSummariesForOwnersAsync(
                        Arg.Any<AttachmentOwner>(),
                        Arg.Any<IReadOnlyCollection<Guid>>(),
                        Arg.Any<CancellationToken>())
                    .Returns(new Dictionary<Guid, AttachmentSummary>());
        _carriers.CountsByCarrierTypeAsync(
                     Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                 .Returns((IReadOnlyDictionary<Guid, int>)new Dictionary<Guid, int> { [shelf.Id] = 8 });

        var result = await new GetCarrierTypesQueryHandler(_types, _attachments, _carriers)
            .Handle(new GetCarrierTypesQuery(), default);

        // One call for the whole page. A count per row is how this list would
        // quietly become N+1 as the catalogue grows.
        await _carriers.Received(1).CountsByCarrierTypeAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { shelf.Id, truck.Id })),
            Arg.Any<CancellationToken>());
        await _carriers.DidNotReceive().CountForCarrierTypeAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        result.Value.Single(r => r.Id == shelf.Id).CarrierCount.Should().Be(8);
        // Absent from the dictionary means none, not unknown.
        result.Value.Single(r => r.Id == truck.Id).CarrierCount.Should().Be(0);
    }

    [Fact]
    public async Task GetByCode_CarriesTheSameCountTheListWouldShow()
    {
        var type = NewType();
        _types.GetByCodeAsync("SHELF", Arg.Any<CancellationToken>()).Returns(type);
        _attachments.GetSummariesForOwnersAsync(
                        Arg.Any<AttachmentOwner>(),
                        Arg.Any<IReadOnlyCollection<Guid>>(),
                        Arg.Any<CancellationToken>())
                    .Returns(new Dictionary<Guid, AttachmentSummary>());
        _carriers.CountForCarrierTypeAsync(type.Id, Arg.Any<CancellationToken>()).Returns(8);

        var result = await new GetCarrierTypeByCodeQueryHandler(_types, _attachments, _carriers)
            .Handle(new GetCarrierTypeByCodeQuery("SHELF"), default);

        result.Value.CarrierCount.Should().Be(8);
    }

    private DeleteCarrierTypeCommandHandler DeleteHandler()
        => new(_types, _carriers, _attachments);

    private static Attachment PhotoOf(Guid carrierTypeId) => Attachment.For(
        AttachmentOwner.CarrierType, carrierTypeId, "dtms-attachments",
        $"carrier-type/{carrierTypeId}/a.jpg", $"carrier-type/{carrierTypeId}/a.thumb.jpg",
        "image/jpeg", 2048, "a.jpg", null, "Ada Ops");
}
