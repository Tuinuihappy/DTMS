using AMR.DeliveryPlanning.Transport.Manual.Application.Queries.GetPodPresignedUrl;
using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class GetPodPresignedUrlHandlerTests
{
    private readonly IObjectStorageService _storage = Substitute.For<IObjectStorageService>();
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly IPodBucketProvider _bucket = Substitute.For<IPodBucketProvider>();

    private GetPodPresignedUrlQueryHandler CreateSut()
    {
        _bucket.PodBucket.Returns("dtms-pod");
        return new GetPodPresignedUrlQueryHandler(_storage, _extensions, _bucket);
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
        var ext = ManualTripExtension.AssignToOperator(tripId, owner, null, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);

        var sut = CreateSut();
        var result = await sut.Handle(
            new GetPodPresignedUrlQuery(tripId, caller, Kind: "pickup"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("different operator");
    }

    [Fact]
    public async Task Handle_Happy_ReturnsKeyAndUrl()
    {
        var tripId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, opId, null, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);
        _storage.GeneratePresignedPutAsync(
            "dtms-pod",
            Arg.Is<string>(k => k.StartsWith($"pod/{tripId}/pickup/")),
            Arg.Any<TimeSpan>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(call => new PresignedUploadUrl(
                UploadUrl: "https://minio.example/dtms-pod/" + call.ArgAt<string>(1),
                ObjectKey: call.ArgAt<string>(1),
                ExpiresAt: DateTime.UtcNow.AddMinutes(10)));

        var sut = CreateSut();
        var result = await sut.Handle(
            new GetPodPresignedUrlQuery(tripId, opId, Kind: "pickup", FileExtension: "jpg"),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ObjectKey.Should().StartWith($"pod/{tripId}/pickup/");
        result.Value.UploadUrl.Should().StartWith("https://minio.example/dtms-pod/");
        result.Value.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }
}
