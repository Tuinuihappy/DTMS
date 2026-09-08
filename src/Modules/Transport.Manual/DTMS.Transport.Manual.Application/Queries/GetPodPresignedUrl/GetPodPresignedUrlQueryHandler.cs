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
    private readonly IUploadLimits _limits;

    // The capture UI re-encodes every photo to JPEG before upload, so the
    // policy can pin one exact type. Pinning it is the point: a presigned PUT
    // could not express any content-type condition at all.
    private const string PodContentType = "image/jpeg";

    public GetPodPresignedUrlQueryHandler(
        IObjectStorageService storage,
        IManualTripExtensionRepository extensions,
        IStorageBuckets bucket,
        IUploadLimits limits)
    {
        _storage = storage;
        _extensions = extensions;
        _bucket = bucket;
        _limits = limits;
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

        // Staged, not final. The bytes are not a proof of delivery until a leg
        // references them, and until this change an upload that no leg ever
        // referenced — a retaken photo, a leg abandoned mid-flow — was written
        // straight to its final key and became permanent garbage that only a
        // database comparison could identify.
        var objectKey = PodObjectKey.GenerateStaging(request.TripId, kind!, request.FileExtension ?? "jpg");

        // Size and type are conditions inside the signature, so MinIO refuses a
        // violating upload before writing anything. The old presigned PUT could
        // authorise only "write to this key" — an oversized body was discovered
        // after it had already landed on a disk that has filled and hung the
        // docker daemon before.
        var upload = await _storage.GeneratePresignedPostAsync(
            bucket: _bucket.Pod,
            objectKey: objectKey,
            constraints: new UploadConstraints(
                ContentType: PodContentType,
                MinBytes: 1,
                MaxBytes: _limits.MaxUploadBytes),
            expiresIn: PresignTtl,
            ct: cancellationToken);

        return Result<PodPresignedUrlDto>.Success(new PodPresignedUrlDto(
            Url: upload.Url,
            Fields: upload.Fields,
            ObjectKey: upload.ObjectKey,
            ExpiresAt: upload.ExpiresAt));
    }
}

