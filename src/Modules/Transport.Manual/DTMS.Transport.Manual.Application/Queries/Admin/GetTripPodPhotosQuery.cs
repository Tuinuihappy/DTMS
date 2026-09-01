using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Queries.Admin;

/// <summary>
/// The photos an operator took when picking up and dropping a trip.
///
/// <para>These have been uploaded since July and have never been visible:
/// the keys were written by the pickup/drop commands and then read by
/// nothing — no query, no DTO, no endpoint — and the bucket is private, so
/// there was no path to the bytes either. This query supplies both halves.</para>
/// </summary>
public record GetTripPodPhotosQuery(Guid TripId) : IQuery<TripPodPhotosDto>;

/// <summary>
/// A URL is null when that leg has no photo — the operator was never forced
/// to take one. The dispatcher UI shows the leg as "no photo" rather than a
/// broken image.
/// </summary>
public record TripPodPhotosDto(
    Guid TripId,
    TripPodPhotoDto? Pickup,
    TripPodPhotoDto? Drop)
{
    public bool HasAny => Pickup is not null || Drop is not null;
}

public record TripPodPhotoDto(string ObjectKey, string Url, DateTime ExpiresAt);

internal sealed class GetTripPodPhotosQueryHandler
    : IQueryHandler<GetTripPodPhotosQuery, TripPodPhotosDto>
{
    // Long enough that a dispatcher can open the drawer, look, and come back
    // without the image dying mid-session. A shorter window buys less than it
    // looks like it does: the signature embeds a timestamp, so every call
    // produces a different URL and the browser re-downloads regardless — the
    // client caches the URL for its lifetime instead of re-asking.
    public static readonly TimeSpan UrlTtl = TimeSpan.FromHours(1);

    // Photos are captured as JPEG and the upload policy pins that type, but
    // the served type is stated explicitly rather than inherited: an object
    // carries whatever content type its uploader sent, and anything uploaded
    // before that policy existed carries whatever it claimed at the time.
    private const string ServedContentType = "image/jpeg";

    private readonly IManualTripExtensionRepository _extensions;
    private readonly IObjectStorageService _storage;
    private readonly IStorageBuckets _buckets;

    public GetTripPodPhotosQueryHandler(
        IManualTripExtensionRepository extensions,
        IObjectStorageService storage,
        IStorageBuckets buckets)
    {
        _extensions = extensions;
        _storage = storage;
        _buckets = buckets;
    }

    public async Task<Result<TripPodPhotosDto>> Handle(
        GetTripPodPhotosQuery request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);

        // A trip run by an AMR has no Manual extension at all. That is an
        // ordinary answer — "this trip has no operator photos" — not a
        // failure, so the caller gets an empty result and the UI simply
        // omits the section.
        if (ext is null)
            return Result<TripPodPhotosDto>.Success(new TripPodPhotosDto(request.TripId, null, null));

        return Result<TripPodPhotosDto>.Success(new TripPodPhotosDto(
            request.TripId,
            await SignAsync(ext.PickupPodKey, cancellationToken),
            await SignAsync(ext.DropPodKey, cancellationToken)));
    }

    private async Task<TripPodPhotoDto?> SignAsync(string? objectKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return null;

        // Deliberately not checked for existence first. A stat would double the
        // round trips for every photo to guard against a case that means the
        // object was deleted out from under us — and the URL would 404 in the
        // browser anyway, which the gallery already has to handle.
        var url = await _storage.GeneratePresignedGetAsync(
            bucket: _buckets.Pod,
            objectKey: objectKey,
            expiresIn: UrlTtl,
            responseContentType: ServedContentType,
            ct: ct);

        return new TripPodPhotoDto(objectKey, url, DateTime.UtcNow.Add(UrlTtl));
    }
}
