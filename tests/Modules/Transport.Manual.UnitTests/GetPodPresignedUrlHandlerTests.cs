using DTMS.SharedKernel.Storage;
using DTMS.Transport.Manual.Application.Queries.GetPodPresignedUrl;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class GetPodPresignedUrlHandlerTests
{
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly IStorageBuckets _bucket = Substitute.For<IStorageBuckets>();
    private readonly IUploadLimits _limits = Substitute.For<IUploadLimits>();

    private GetPodPresignedUrlQueryHandler CreateSut()
    {
        _bucket.Pod.Returns("dtms-pod");
        _limits.MaxUploadBytes.Returns(10L * 1024 * 1024);
        return new GetPodPresignedUrlQueryHandler(_storage, _extensions, _bucket, _limits);
    }

    [Fact]
    public async Task Handle_InvalidKind_Fails()
    {
        var sut = CreateSut();
        var result = await sut.Handle(
            new GetPodPresignedUrlQuery(Guid.NewGuid(), Guid.NewGuid(), Kind: "completed"),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("pickup");
    }

    [Fact]
    public async Task Handle_TripWithoutExtension_Fails()
    {
        _extensions.GetByTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                   .Returns((ManualTripExtension?)null);
        var sut = CreateSut();
        var result = await sut.Handle(
            new GetPodPresignedUrlQuery(Guid.NewGuid(), Guid.NewGuid(), Kind: "pickup"),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no Manual extension");
    }

    [Fact]
    public async Task Handle_TripOwnedByOtherOperator_Fails()
    {
        var tripId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, owner, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);

        var sut = CreateSut();
        var result = await sut.Handle(
            new GetPodPresignedUrlQuery(tripId, caller, Kind: "pickup"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("different operator");
    }

    [Fact]
    public async Task Handle_Happy_ReturnsKeyAndSignedForm()
    {
        var tripId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, opId, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);
        StubPresign(tripId);

        var sut = CreateSut();
        var result = await sut.Handle(
            new GetPodPresignedUrlQuery(tripId, opId, Kind: "pickup", FileExtension: "jpg"),
            default);

        result.IsSuccess.Should().BeTrue();
        // Staged, not final: the bytes become a proof of delivery only when a
        // leg references them, and RecordPickup is what promotes them.
        result.Value.ObjectKey.Should().StartWith($"incoming/pod/{tripId}/pickup/");
        result.Value.Url.Should().StartWith("https://minio.example/dtms-pod");
        result.Value.Fields.Should().ContainKey("policy");
        result.Value.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    // The whole reason for moving off a presigned PUT: a PUT signature carries
    // no conditions, so an oversized or wrong-typed body was only detectable
    // after MinIO had already written it. This is what stops a later refactor
    // from quietly dropping the conditions and restoring that hole.
    [Fact]
    public async Task Handle_PinsSizeCeilingAndExactContentType_IntoThePolicy()
    {
        var tripId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>())
                   .Returns(ManualTripExtension.AssignToOperator(tripId, opId, null, null));
        StubPresign(tripId);

        var sut = CreateSut();
        await sut.Handle(new GetPodPresignedUrlQuery(tripId, opId, Kind: "pickup"), default);

        await _storage.Received(1).GeneratePresignedPostAsync(
            "dtms-pod",
            Arg.Any<string>(),
            Arg.Is<UploadConstraints>(c =>
                c.ContentType == "image/jpeg" &&
                c.MinBytes == 1 &&
                c.MaxBytes == 10L * 1024 * 1024),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    private void StubPresign(Guid tripId) =>
        _storage.GeneratePresignedPostAsync(
            "dtms-pod",
            Arg.Is<string>(k => k.StartsWith($"incoming/pod/{tripId}/")),
            Arg.Any<UploadConstraints>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns(call => new PresignedUpload(
                Url: "https://minio.example/dtms-pod",
                Fields: new Dictionary<string, string>
                {
                    ["key"] = call.ArgAt<string>(1),
                    ["policy"] = "stub"
                },
                ObjectKey: call.ArgAt<string>(1),
                ExpiresAt: DateTime.UtcNow.AddMinutes(10)));
}
