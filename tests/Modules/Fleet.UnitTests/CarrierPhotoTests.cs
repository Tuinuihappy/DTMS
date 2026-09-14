using DTMS.Fleet.Application.Queries.GetAttachmentThumbnail;
using DTMS.Fleet.Application.Queries.GetCarrierByCode;
using DTMS.Fleet.Application.Queries.GetCarriers;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Storage;
using FluentAssertions;
using NSubstitute;

namespace Fleet.UnitTests;

/// <summary>
/// Guards the rule for which photo stands for an owner. The gallery orders its
/// images the same way in SQL, so if this drifts the thumbnail in a table row
/// stops being the first picture in that row's gallery.
/// </summary>
public class AttachmentSummaryTests
{
    private static readonly DateTime T0 = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Summarize_PicksTheNewestImage_AndCountsThemAll()
    {
        var owner = Guid.NewGuid();
        var oldest = Guid.NewGuid();
        var newest = Guid.NewGuid();

        var result = AttachmentSummary.Summarize(
        [
            (owner, oldest, T0),
            (owner, newest, T0.AddMinutes(5)),
            (owner, Guid.NewGuid(), T0.AddMinutes(1)),
        ]);

        result[owner].Should().Be(new AttachmentSummary(newest, 3));
    }

    // Two uploads can land in the same instant. Without a tiebreaker the cover
    // would depend on row order and could change between requests.
    [Fact]
    public void Summarize_BreaksATieOnUploadTime_ByTheLargerId()
    {
        var owner = Guid.NewGuid();
        var low = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var high = Guid.Parse("f0000000-0000-0000-0000-000000000000");

        AttachmentSummary.Summarize([(owner, low, T0), (owner, high, T0)])[owner].CoverId
            .Should().Be(high);
        AttachmentSummary.Summarize([(owner, high, T0), (owner, low, T0)])[owner].CoverId
            .Should().Be(high, "the answer must not depend on the order rows arrive in");
    }

    [Fact]
    public void Summarize_KeepsEachOwnerSeparate()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var aImage = Guid.NewGuid();
        var bImage = Guid.NewGuid();

        var result = AttachmentSummary.Summarize(
        [
            (a, aImage, T0),
            (b, bImage, T0),
            (b, Guid.NewGuid(), T0.AddMinutes(-1)),
        ]);

        result[a].Should().Be(new AttachmentSummary(aImage, 1));
        result[b].Should().Be(new AttachmentSummary(bImage, 2));
    }

    [Fact]
    public void Summarize_OfNothing_IsEmpty()
        => AttachmentSummary.Summarize([]).Should().BeEmpty();
}

public class CarrierListPhotoTests
{
    private readonly ICarrierRepository _carriers = Substitute.For<ICarrierRepository>();
    private readonly ICarrierTypeRepository _types = Substitute.For<ICarrierTypeRepository>();
    private readonly IAttachmentRepository _attachments = Substitute.For<IAttachmentRepository>();
    private readonly CarrierType _shelf = new("SHELF", "Shelf", "LIFT");

    private Carrier NewCarrier(string code) => new(code, _shelf.Id, null, null, null, "tester");

    private void GivenPage(params Carrier[] rows)
    {
        _types.GetAllAsync(Arg.Any<CancellationToken>()).Returns([_shelf]);
        _carriers.SearchAsync(
                Arg.Any<CarrierStatus?>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Carrier>)rows, rows.Length));
    }

    private void GivenPhotos(Dictionary<Guid, AttachmentSummary> summaries)
        => _attachments.GetSummariesForOwnersAsync(
                Arg.Any<AttachmentOwner>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<Guid, AttachmentSummary>)summaries);

    // The whole point of the column: one query for the page, never one per row.
    [Fact]
    public async Task List_LooksUpPhotosOnce_ForExactlyThePagesCarriers()
    {
        var first = NewCarrier("SHELF-001");
        var second = NewCarrier("SHELF-002");
        GivenPage(first, second);
        GivenPhotos([]);

        await new GetCarriersQueryHandler(_carriers, _types, _attachments)
            .Handle(new GetCarriersQuery(), default);

        await _attachments.Received(1).GetSummariesForOwnersAsync(
            AttachmentOwner.Carrier,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { first.Id, second.Id })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_PutsEachCoverOnItsOwnRow_AndLeavesPhotolessRowsEmpty()
    {
        var withPhotos = NewCarrier("SHELF-001");
        var without = NewCarrier("SHELF-002");
        var cover = Guid.NewGuid();
        GivenPage(withPhotos, without);
        GivenPhotos(new() { [withPhotos.Id] = new AttachmentSummary(cover, 3) });

        var result = await new GetCarriersQueryHandler(_carriers, _types, _attachments)
            .Handle(new GetCarriersQuery(), default);

        var rows = result.Value.Data;
        rows.Single(r => r.Id == withPhotos.Id).Should().Match<CarrierListDto>(r =>
            r.CoverAttachmentId == cover && r.PhotoCount == 3);
        rows.Single(r => r.Id == without.Id).Should().Match<CarrierListDto>(r =>
            r.CoverAttachmentId == null && r.PhotoCount == 0);
    }

    // The list and the detail share a DTO. A detail that said "no photos" while
    // the row showed three would be believed by whatever reads it next.
    [Fact]
    public async Task Detail_CarriesTheSamePhotoFieldsAsTheList()
    {
        var carrier = NewCarrier("SHELF-001");
        var cover = Guid.NewGuid();
        _carriers.GetByCodeAsync("SHELF-001", Arg.Any<CancellationToken>()).Returns(carrier);
        _types.GetByIdAsync(_shelf.Id, Arg.Any<CancellationToken>()).Returns(_shelf);
        GivenPhotos(new() { [carrier.Id] = new AttachmentSummary(cover, 2) });

        var result = await new GetCarrierByCodeQueryHandler(_carriers, _types, _attachments)
            .Handle(new GetCarrierByCodeQuery("SHELF-001"), default);

        result.Value.CoverAttachmentId.Should().Be(cover);
        result.Value.PhotoCount.Should().Be(2);
    }
}

public class GetAttachmentThumbnailHandlerTests
{
    private readonly IAttachmentRepository _attachments = Substitute.For<IAttachmentRepository>();
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();

    private GetAttachmentThumbnailQueryHandler Sut()
    {
        _storage.GeneratePresignedGetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("http://minio:9000/signed");
        return new GetAttachmentThumbnailQueryHandler(_attachments, _storage);
    }

    private void GivenImage(Attachment image)
        => _attachments.FindForOwnerAsync(
                AttachmentOwner.Carrier, image.OwnerId, image.Id, Arg.Any<CancellationToken>())
            .Returns(image);

    private static Attachment Image(string? thumbnailKey) => Attachment.For(
        AttachmentOwner.Carrier, Guid.NewGuid(), "dtms-attachments",
        "carrier/x/abc.jpg", thumbnailKey, "image/webp", 100, null, null, "someone");

    [Fact]
    public async Task SignsTheThumbnail_ForMinutes_AsTheRecordedType()
    {
        var sut = Sut();
        var image = Image("carrier/x/abc.thumb.jpg");
        GivenImage(image);

        var result = await sut.Handle(
            new GetAttachmentThumbnailQuery(AttachmentOwner.Carrier, image.OwnerId, image.Id), default);

        result.Value.Should().Be("http://minio:9000/signed");
        await _storage.Received(1).GeneratePresignedGetAsync(
            "dtms-attachments", "carrier/x/abc.thumb.jpg",
            GetAttachmentThumbnailQueryHandler.UrlTtl, "image/webp", Arg.Any<CancellationToken>());
        GetAttachmentThumbnailQueryHandler.UrlTtl.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5),
            "the URL is used once, server-side, the moment it is issued");
    }

    // Some browsers cannot decode an input to make a thumbnail at upload time.
    [Fact]
    public async Task FallsBackToTheFullImage_WhenThereIsNoThumbnail()
    {
        var sut = Sut();
        var image = Image(thumbnailKey: null);
        GivenImage(image);

        await sut.Handle(
            new GetAttachmentThumbnailQuery(AttachmentOwner.Carrier, image.OwnerId, image.Id), default);

        await _storage.Received(1).GeneratePresignedGetAsync(
            Arg.Any<string>(), "carrier/x/abc.jpg", Arg.Any<TimeSpan>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // The route is guarded by one owner kind's permission. An id that is not that
    // owner's — missing, or someone else's — must not be signed at all.
    [Fact]
    public async Task AnImageThatIsNotThisOwners_IsNotFound_AndNothingIsSigned()
    {
        var sut = Sut();
        _attachments.FindForOwnerAsync(
                Arg.Any<AttachmentOwner>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Attachment?)null);

        var result = await sut.Handle(
            new GetAttachmentThumbnailQuery(AttachmentOwner.Carrier, Guid.NewGuid(), Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        await _storage.DidNotReceiveWithAnyArgs().GeneratePresignedGetAsync(
            default!, default!, default, default, default);
    }
}
