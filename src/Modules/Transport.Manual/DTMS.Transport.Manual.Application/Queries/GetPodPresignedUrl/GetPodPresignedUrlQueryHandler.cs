using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;
using DTMS.Transport.Manual.Application.Services; // PodObjectKey
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Queries.GetPodPresignedUrl;

internal sealed class GetPodPresignedUrlQueryHandler
    : IQueryHandler<GetPodPresignedUrlQuery, PodPresignedUrlDto>
{
    // Per ADR-015 — short TTL keeps the attack window narrow if a URL
    // leaks. 10 minutes covers normal-network upload + retry; if a
    // delivery genuinely takes longer the operator app re-presigns.
    public static readonly TimeSpan PresignTtl = TimeSpan.FromMinutes(10);

    // Bucket names live in config (ObjectStorage:*) but reach Application as
    // plain strings through IStorageBuckets, so this layer stays free of a
    // Microsoft.Extensions.Options dependency.
    private readonly IObjectStorageService _storage;
    private readonly IManualTripExtensionRepository _extensions;
    private readonly IStorageBuckets _bucket;

    public GetPodPresignedUrlQueryHandler(
        IObjectStorageService storage,
        IManualTripExtensionRepository extensions,
        IStorageBuckets bucket)
    {
        _storage = storage;
        _extensions = extensions;
        _bucket = bucket;
    }

    public async Task<Result<PodPresignedUrlDto>> Handle(
        GetPodPresignedUrlQuery request, CancellationToken cancellationToken)
    {
        var kind = request.Kind?.ToLowerInvariant();
        if (kind != PodObjectKey.KindPickup && kind != PodObjectKey.KindDrop)
            return Result<PodPresignedUrlDto>.Failure("Kind must be 'pickup' or 'drop'.");

        // Make sure the trip is actually assigned to this operator —
        // otherwise anyone could request presigned URLs for any trip.
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result<PodPresignedUrlDto>.Failure(
                $"Trip {request.TripId} has no Manual extension — operator can't presign.");
        if (ext.OperatorId != request.OperatorId)
            return Result<PodPresignedUrlDto>.Failure("Trip is assigned to a different operator.");

        var objectKey = PodObjectKey.Generate(request.TripId, kind!, request.FileExtension ?? "jpg");
        var url = await _storage.GeneratePresignedPutAsync(
            bucket: _bucket.Pod,
            objectKey: objectKey,
            expiresIn: PresignTtl,
            contentType: "image/jpeg",
            ct: cancellationToken);

        return Result<PodPresignedUrlDto>.Success(new PodPresignedUrlDto(
            UploadUrl: url.UploadUrl,
            ObjectKey: url.ObjectKey,
            ExpiresAt: url.ExpiresAt));
    }
}

