using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Repositories;

// Note: IPodBucketProvider lives in Services/IPodBucketProvider.cs.

namespace DTMS.Transport.Manual.Application.Queries.GetPodPresignedUrl;

internal sealed class GetPodPresignedUrlQueryHandler
    : IQueryHandler<GetPodPresignedUrlQuery, PodPresignedUrlDto>
{
    // Per ADR-015 — short TTL keeps the attack window narrow if a URL
    // leaks. 10 minutes covers normal-network upload + retry; if a
    // delivery genuinely takes longer the operator app re-presigns.
    public static readonly TimeSpan PresignTtl = TimeSpan.FromMinutes(10);

    // Bucket name lives in config (ObjectStorage:PodBucket) — but Phase
    // 4.3 keeps it injected as a string instead of pulling IOptions into
    // Application (which would force a Microsoft.Extensions.Options
    // dependency). Instead the Infrastructure-side wire-up resolves the
    // bucket from config and registers a delegate factory.
    private readonly IObjectStorageService _storage;
    private readonly IManualTripExtensionRepository _extensions;
    private readonly IPodBucketProvider _bucket;

    public GetPodPresignedUrlQueryHandler(
        IObjectStorageService storage,
        IManualTripExtensionRepository extensions,
        IPodBucketProvider bucket)
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
            bucket: _bucket.PodBucket,
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

