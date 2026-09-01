using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Entities;
using FluentAssertions;

namespace Fleet.UnitTests;

public class AttachmentTests
{
    private static Attachment Make(
        AttachmentOwner owner = AttachmentOwner.Carrier,
        string? thumbnailKey = "carrier/x/abc.thumb.jpg",
        string uploadedBy = "somebody") =>
        Attachment.For(
            owner, Guid.NewGuid(), "dtms-attachments",
            "carrier/x/abc.jpg", thumbnailKey,
            "image/jpeg", 1234, "photo.jpg", null, uploadedBy);

    [Theory]
    [InlineData(AttachmentOwner.Carrier)]
    [InlineData(AttachmentOwner.CarrierType)]
    [InlineData(AttachmentOwner.MaintenanceLog)]
    public void For_SetsExactlyOneOwnerColumn(AttachmentOwner owner)
    {
        var a = Make(owner);

        // The whole reason for three columns instead of a polymorphic pair is
        // that each can carry a real foreign key. That only holds if exactly
        // one is ever populated.
        var populated = new[] { a.CarrierId, a.CarrierTypeId, a.MaintenanceLogId }
            .Count(v => v is not null);

        populated.Should().Be(1);
        a.Owner.Should().Be(owner);
        a.OwnerId.Should().NotBeEmpty();
    }

    [Fact]
    public void For_RejectsEmptyOwnerId()
    {
        var act = () => Attachment.For(
            AttachmentOwner.Carrier, Guid.Empty, "b", "k", null, "image/jpeg", 1, null, null, "x");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void For_RejectsNonPositiveSize()
    {
        // Size comes from the storage server's own measurement, so zero means
        // something went wrong upstream rather than an empty-but-valid image.
        var act = () => Attachment.For(
            AttachmentOwner.Carrier, Guid.NewGuid(), "b", "k", null, "image/jpeg", 0, null, null, "x");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void For_BlankUploader_BecomesSystem()
    {
        // A NOT NULL column accepts "" happily, leaving a row that records who
        // did nothing at all.
        Make(uploadedBy: "   ").UploadedBy.Should().Be("system");
    }

    [Fact]
    public void AllObjectKeys_IncludesThumbnail_SoDeletionCannotMissIt()
    {
        Make().AllObjectKeys().Should().HaveCount(2);
        Make(thumbnailKey: null).AllObjectKeys().Should().ContainSingle();
    }
}

public class AttachmentObjectKeyTests
{
    [Fact]
    public void Staging_KeysLiveUnderTheIncomingPrefix()
    {
        var id = Guid.NewGuid();

        // Everything under this prefix is expendable by definition, which is
        // what lets a storage lifecycle rule clear abandoned uploads without
        // any code holding the power to delete real objects.
        AttachmentObjectKey.Staging(id).Should().StartWith(AttachmentObjectKey.IncomingPrefix);
        AttachmentObjectKey.StagingThumbnail(id).Should().StartWith(AttachmentObjectKey.IncomingPrefix);
    }

    [Fact]
    public void Final_KeysAreNeverUnderTheStagingPrefix()
    {
        var key = AttachmentObjectKey.Final(AttachmentOwner.Carrier, Guid.NewGuid(), Guid.NewGuid(), ".jpg");

        key.Should().NotStartWith(AttachmentObjectKey.IncomingPrefix);
        key.Should().StartWith("carrier/");
    }

    [Fact]
    public void Final_And_Thumbnail_Differ()
    {
        var owner = Guid.NewGuid();
        var upload = Guid.NewGuid();

        AttachmentObjectKey.Final(AttachmentOwner.Carrier, owner, upload, ".jpg")
            .Should().NotBe(AttachmentObjectKey.FinalThumbnail(AttachmentOwner.Carrier, owner, upload, ".jpg"));
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    public void ExtensionFor_DerivesFromContentType_NotFileName(string contentType, string expected)
    {
        // The uploaded file name is attacker-supplied and the extension lands
        // in a URL, so it is derived from the type the policy pinned instead.
        AttachmentObjectKey.ExtensionFor(contentType).Should().Be(expected);
    }
}
