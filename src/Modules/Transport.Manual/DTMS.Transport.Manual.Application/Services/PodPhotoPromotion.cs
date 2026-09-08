using DTMS.SharedKernel.Storage;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Manual.Application.Services;

/// <summary>
/// What happened to the photo key an operator submitted with a leg.
/// <see cref="Key"/> is what gets stored — null means the leg is recorded
/// without a photo, which has always been an accepted state.
/// </summary>
internal readonly record struct PodPhotoResolution(string? Error, string? Key)
{
    public bool IsFailure => Error is not null;

    public static PodPhotoResolution Rejected(string error) => new(error, null);
    public static PodPhotoResolution Recorded(string? key) => new(null, key);
}

/// <summary>
/// Moves a submitted POD photo out of staging and into its final key.
///
/// <para>Static rather than an injected service: it is a decision about two
/// strings and one copy, both pickup and drop need the identical wording, and
/// Transport.Manual has no DI module of its own — only MediatR handlers are
/// discovered, so a class here would need hand-registering at the composition
/// root for no gain.</para>
/// </summary>
internal static class PodPhotoPromotion
{
    public static async Task<PodPhotoResolution> ResolveAsync(
        IObjectStorageService storage,
        string bucket,
        string? submittedKey,
        Guid tripId,
        string kind,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(submittedKey))
            return PodPhotoResolution.Recorded(null);

        // The check PodObjectKey has always documented and nothing ever called.
        // Without it the key is a caller-supplied string written verbatim onto
        // the leg, so one operator could stamp another trip's photo onto their
        // own delivery — or any string at all onto a proof of delivery.
        if (!PodObjectKey.BelongsToTripLeg(submittedKey, tripId, kind))
        {
            logger.LogWarning(
                "POD key '{Key}' does not belong to trip {TripId} leg '{Kind}' — refused.",
                submittedKey, tripId, kind);
            return PodPhotoResolution.Rejected("That photo does not belong to this trip leg.");
        }

        var finalKey = PodObjectKey.PromoteToFinal(submittedKey);

        // Already final. Happens for a URL presigned just before this change
        // shipped and submitted just after, and costs one branch to tolerate
        // rather than failing a leg over a deploy boundary.
        if (finalKey is null)
            return PodPhotoResolution.Recorded(submittedKey);

        // Asked before copying so the two reasons a copy fails stay apart. An
        // object that is not there cannot be recovered by retrying — the
        // capture was abandoned, or it outlived the staging expiry while the
        // action sat in the offline queue — and refusing the leg over it would
        // strand an operator who is no longer at the location. Storage being
        // briefly unreachable is the opposite: retrying is exactly right, and
        // recording the leg without the photo would throw away a proof of
        // delivery that still exists.
        if (!await storage.ObjectExistsAsync(bucket, submittedKey, ct))
        {
            logger.LogWarning(
                "POD photo {Bucket}/{Key} was gone when trip {TripId} leg '{Kind}' was recorded; " +
                "the leg is saved without it.",
                bucket, submittedKey, tripId, kind);
            return PodPhotoResolution.Recorded(null);
        }

        try
        {
            await storage.CopyAsync(bucket, submittedKey, finalKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not promote POD photo {Bucket}/{Key} for trip {TripId} leg '{Kind}'.",
                bucket, submittedKey, tripId, kind);
            return PodPhotoResolution.Rejected(
                "Couldn't save the photo. Try again — nothing was recorded.");
        }

        await TryDeleteStagingAsync(storage, bucket, submittedKey, logger, ct);
        return PodPhotoResolution.Recorded(finalKey);
    }

    private static async Task TryDeleteStagingAsync(
        IObjectStorageService storage, string bucket, string key, ILogger logger, CancellationToken ct)
    {
        // Genuinely best effort: the copy already succeeded, so the proof is
        // safe at its final key and a surviving staging duplicate is collected
        // by the bucket's expiry rule. Failing here must never turn a recorded
        // leg into an error.
        try
        {
            if (await storage.DeleteAsync(bucket, key, ct) == ObjectDeleteOutcome.Failed)
            {
                logger.LogDebug(
                    "Staging POD object {Bucket}/{Key} was not cleared; the expiry rule will collect it.",
                    bucket, key);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex,
                "Could not clear staging POD object {Bucket}/{Key}; the expiry rule will collect it.",
                bucket, key);
        }
    }
}
