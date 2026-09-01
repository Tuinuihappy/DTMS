using DTMS.Fleet.Application.Commands.ConfirmAttachment;
using DTMS.Fleet.Application.Commands.DeleteAttachment;
using DTMS.Fleet.Application.Commands.PresignAttachment;
using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Events;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Fleet.UnitTests;

public class PresignAttachmentHandlerTests
{
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly IStorageBuckets _buckets = Substitute.For<IStorageBuckets>();
    private readonly IUploadLimits _limits = Substitute.For<IUploadLimits>();
    private readonly IAttachmentOwnerLookup _owners = Substitute.For<IAttachmentOwnerLookup>();
    private readonly IAttachmentRepository _attachments = Substitute.For<IAttachmentRepository>();

    private PresignAttachmentCommandHandler Sut()
    {
        _buckets.Attachments.Returns("dtms-attachments");
        _limits.MaxUploadBytes.Returns(10L * 1024 * 1024);
        _limits.MaxAttachmentsPerOwner.Returns(10);
        _limits.AllowedContentTypes.Returns(new[] { "image/jpeg", "image/png", "image/webp" });
        _limits.IsAllowed(Arg.Any<string>()).Returns(c =>
            new[] { "image/jpeg", "image/png", "image/webp" }.Contains(c.ArgAt<string>(0)));
        _owners.ExistsAsync(Arg.Any<AttachmentOwner>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(true);
        _storage.GeneratePresignedPostAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<UploadConstraints>(),
                Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => new PresignedUpload(
                "https://minio.example/dtms-attachments",
                new Dictionary<string, string> { ["policy"] = "stub" },
                call.ArgAt<string>(1),
                DateTime.UtcNow.AddMinutes(10)));

        return new PresignAttachmentCommandHandler(_storage, _buckets, _limits, _owners, _attachments);
    }

    // An "image/" prefix would let this through, and an SVG served under its own
    // type executes script on the storage origin.
    [Fact]
    public async Task Svg_IsRefused_BeforeAnyUploadIsAuthorised()
    {
        var result = await Sut().Handle(
            new PresignAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), "image/svg+xml", true),
            default);

        result.IsFailure.Should().BeTrue();
        await _storage.DidNotReceiveWithAnyArgs().GeneratePresignedPostAsync(
            default!, default!, default!, default, default);
    }

    [Fact]
    public async Task UnknownOwner_IsRefused_RatherThanLeftToTheForeignKey()
    {
        // Built first — Sut() installs the default stubs, so overriding before
        // it would be undone.
        var sut = Sut();
        _owners.ExistsAsync(Arg.Any<AttachmentOwner>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(false);

        var result = await sut.Handle(
            new PresignAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), "image/jpeg", true),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AtTheCeiling_IsRefused_BeforeTheUserUploads()
    {
        _attachments.CountForOwnerAsync(Arg.Any<AttachmentOwner>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(10);

        var result = await Sut().Handle(
            new PresignAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), "image/jpeg", true),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("maximum");
    }

    [Fact]
    public async Task Keys_PointAtStaging_NotTheFinalPrefix()
    {
        var result = await Sut().Handle(
            new PresignAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), "image/jpeg", true),
            default);

        result.IsSuccess.Should().BeTrue();
        // The final prefix must only ever hold confirmed objects — that is what
        // makes an abandoned upload identifiable without consulting the database.
        result.Value.Image.ObjectKey.Should().StartWith(AttachmentObjectKey.IncomingPrefix);
        result.Value.Thumbnail!.ObjectKey.Should().StartWith(AttachmentObjectKey.IncomingPrefix);
    }

    [Fact]
    public async Task SizeCeiling_IsPinnedIntoThePolicy()
    {
        await Sut().Handle(
            new PresignAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), "image/jpeg", false),
            default);

        await _storage.Received(1).GeneratePresignedPostAsync(
            "dtms-attachments",
            Arg.Any<string>(),
            Arg.Is<UploadConstraints>(c =>
                c.ContentType == "image/jpeg" && c.MinBytes == 1 && c.MaxBytes == 10L * 1024 * 1024),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }
}

public class ConfirmAttachmentHandlerTests
{
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly IStorageBuckets _buckets = Substitute.For<IStorageBuckets>();
    private readonly IUploadLimits _limits = Substitute.For<IUploadLimits>();
    private readonly IAttachmentOwnerLookup _owners = Substitute.For<IAttachmentOwnerLookup>();
    private readonly IAttachmentRepository _attachments = Substitute.For<IAttachmentRepository>();
    private readonly ICurrentActorContext _actor = Substitute.For<ICurrentActorContext>();

    private ConfirmAttachmentCommandHandler Sut()
    {
        _buckets.Attachments.Returns("dtms-attachments");
        _limits.MaxAttachmentsPerOwner.Returns(10);
        _limits.IsAllowed(Arg.Any<string>()).Returns(c =>
            new[] { "image/jpeg", "image/png", "image/webp" }.Contains(c.ArgAt<string>(0)));
        _owners.ExistsAsync(Arg.Any<AttachmentOwner>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(true);
        _actor.Current.Returns(ActorContext.System);

        return new ConfirmAttachmentCommandHandler(
            _storage, _buckets, _limits, _owners, _attachments, _actor,
            NullLogger<ConfirmAttachmentCommandHandler>.Instance);
    }

    private void StubStat(bool exists, long size = 2048, string contentType = "image/jpeg") =>
        _storage.StatAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ObjectMetadata(exists, size, contentType));

    [Fact]
    public async Task MissingObject_WritesNothing()
    {
        StubStat(exists: false);

        var result = await Sut().Handle(
            new ConfirmAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), Guid.NewGuid(), false),
            default);

        result.IsFailure.Should().BeTrue();
        await _attachments.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _storage.DidNotReceiveWithAnyArgs().CopyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SizeAndType_ComeFromStorage_NotFromTheRequest()
    {
        StubStat(exists: true, size: 4096, contentType: "image/png");
        Attachment? saved = null;
        await _attachments.AddAsync(Arg.Do<Attachment>(a => saved = a), Arg.Any<CancellationToken>());

        var result = await Sut().Handle(
            new ConfirmAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), Guid.NewGuid(), false),
            default);

        result.IsSuccess.Should().BeTrue();
        // The bytes went browser-to-storage, so the only trustworthy account of
        // what landed is storage's own.
        saved!.SizeBytes.Should().Be(4096);
        saved.ContentType.Should().Be("image/png");
        saved.ObjectKey.Should().EndWith(".png");
    }

    [Fact]
    public async Task ObjectIsPromotedOutOfStaging_BeforeTheRowIsWritten()
    {
        StubStat(exists: true);

        await Sut().Handle(
            new ConfirmAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), Guid.NewGuid(), false),
            default);

        await _storage.Received(1).CopyAsync(
            "dtms-attachments",
            Arg.Is<string>(k => k.StartsWith(AttachmentObjectKey.IncomingPrefix)),
            Arg.Is<string>(k => !k.StartsWith(AttachmentObjectKey.IncomingPrefix)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoredTypeOutsideTheAllowList_IsRefused()
    {
        // Belt and braces: an object that reached the bucket by some route other
        // than our upload policy must not become a row.
        StubStat(exists: true, contentType: "image/svg+xml");

        var result = await Sut().Handle(
            new ConfirmAttachmentCommand(AttachmentOwner.Carrier, Guid.NewGuid(), Guid.NewGuid(), false),
            default);

        result.IsFailure.Should().BeTrue();
        await _attachments.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}

public class DeleteAttachmentHandlerTests
{
    private readonly IAttachmentRepository _attachments = Substitute.For<IAttachmentRepository>();

    private static Attachment Existing() => Attachment.For(
        AttachmentOwner.Carrier, Guid.NewGuid(), "dtms-attachments",
        "carrier/x/abc.jpg", "carrier/x/abc.thumb.jpg",
        "image/jpeg", 100, null, null, "someone");

    // Deleting the row makes the image vanish from the screen, which looks like
    // success while the bytes stay in the bucket forever. This is the test that
    // stops that from being reintroduced.
    [Fact]
    public async Task Delete_RaisesOrphanEvent_CoveringEveryObjectKey()
    {
        var attachment = Existing();
        _attachments.GetByIdAsync(attachment.Id, Arg.Any<CancellationToken>()).Returns(attachment);

        var sut = new DeleteAttachmentCommandHandler(_attachments);
        var result = await sut.Handle(new DeleteAttachmentCommand(attachment.Id), default);

        result.IsSuccess.Should().BeTrue();

        // Left on the aggregate for the DbContext interceptor to drain into the
        // outbox during SaveChanges — the handler must not write outbox rows
        // itself, which ModuleBoundaryTests enforces.
        var evt = attachment.DomainEvents
            .OfType<AttachmentObjectsOrphanedDomainEvent>()
            .Should().ContainSingle().Subject;

        evt.Bucket.Should().Be("dtms-attachments");
        evt.ObjectKeys.Should().BeEquivalentTo(["carrier/x/abc.jpg", "carrier/x/abc.thumb.jpg"]);

        _attachments.Received(1).Remove(attachment);
        await _attachments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RaisesTheEventBeforeRemoving_SoTheTrackerStillHasIt()
    {
        var attachment = Existing();
        _attachments.GetByIdAsync(attachment.Id, Arg.Any<CancellationToken>()).Returns(attachment);

        var hadEventAtRemoval = false;
        _attachments.When(r => r.Remove(Arg.Any<Attachment>()))
                    .Do(_ => hadEventAtRemoval = attachment.DomainEvents.Count > 0);

        var sut = new DeleteAttachmentCommandHandler(_attachments);
        await sut.Handle(new DeleteAttachmentCommand(attachment.Id), default);

        hadEventAtRemoval.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownId_ChangesNothing()
    {
        _attachments.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns((Attachment?)null);

        var sut = new DeleteAttachmentCommandHandler(_attachments);
        var result = await sut.Handle(new DeleteAttachmentCommand(Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        await _attachments.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
