using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;

namespace DTMS.Fleet.Application.Commands.PresignAttachment;

/// <summary>
/// Authorises one upload. Writes nothing: a row created here would be stranded
/// the moment the user closed the tab, whereas a row created at confirm exists
/// only for bytes that actually arrived.
/// </summary>
public record PresignAttachmentCommand(
    AttachmentOwner Owner,
    Guid OwnerId,
    string ContentType,
    bool WithThumbnail) : ICommand<PresignAttachmentResult>;

/// <summary>
/// <paramref name="UploadId"/> ties the two uploads together and is handed back
/// on confirm; the server derives every key from it, so the client never names
/// an object.
/// </summary>
public record PresignAttachmentResult(
    Guid UploadId,
    PresignedTarget Image,
    PresignedTarget? Thumbnail);

public record PresignedTarget(
    string Url,
    IReadOnlyDictionary<string, string> Fields,
    string ObjectKey,
    DateTime ExpiresAt);

internal sealed class PresignAttachmentCommandHandler
    : ICommandHandler<PresignAttachmentCommand, PresignAttachmentResult>
{
    // Short: the window in which a leaked upload authorisation is usable.
    public static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(10);

    // A thumbnail is a downscale of an image that already passed the ceiling,
    // so it needs no ceiling of its own beyond a sane bound.
    private const long ThumbnailMaxBytes = 512 * 1024;

    private readonly IObjectStorageService _storage;
    private readonly IStorageBuckets _buckets;
    private readonly IUploadLimits _limits;
    private readonly IAttachmentOwnerLookup _owners;
    private readonly IAttachmentRepository _attachments;

    public PresignAttachmentCommandHandler(
        IObjectStorageService storage,
        IStorageBuckets buckets,
        IUploadLimits limits,
        IAttachmentOwnerLookup owners,
        IAttachmentRepository attachments)
    {
        _storage = storage;
        _buckets = buckets;
        _limits = limits;
        _owners = owners;
        _attachments = attachments;
    }

    public async Task<Result<PresignAttachmentResult>> Handle(
        PresignAttachmentCommand request, CancellationToken cancellationToken)
    {
        // An exact allow-list, not an "image/" prefix: that would admit
        // image/svg+xml, and an SVG served under its own type executes script
        // on the storage origin.
        if (!_limits.IsAllowed(request.ContentType))
            return Result<PresignAttachmentResult>.Failure(
                $"'{request.ContentType}' is not an accepted image type. " +
                $"Use one of: {string.Join(", ", _limits.AllowedContentTypes)}.");

        if (!await _owners.ExistsAsync(request.Owner, request.OwnerId, cancellationToken))
            return Result<PresignAttachmentResult>.Failure(
                $"{request.Owner} '{request.OwnerId}' not found.");

        // Fails early so the user is told before uploading rather than after.
        // Confirm checks again; two concurrent confirms can still both pass,
        // which is left alone deliberately — see ConfirmAttachmentCommand.
        var existing = await _attachments.CountForOwnerAsync(
            request.Owner, request.OwnerId, cancellationToken);
        if (existing >= _limits.MaxAttachmentsPerOwner)
            return Result<PresignAttachmentResult>.Failure(
                $"This already has {existing} images, the maximum is {_limits.MaxAttachmentsPerOwner}. " +
                "Remove one before adding another.");

        var uploadId = Guid.NewGuid();

        // Staging, not the final key. Anything that never reaches confirm stays
        // here and is expired by a storage lifecycle rule, so the final prefix
        // only ever holds objects a row points at.
        var image = await SignAsync(
            AttachmentObjectKey.Staging(uploadId),
            request.ContentType, _limits.MaxUploadBytes, cancellationToken);

        PresignedTarget? thumbnail = null;
        if (request.WithThumbnail)
        {
            thumbnail = await SignAsync(
                AttachmentObjectKey.StagingThumbnail(uploadId),
                request.ContentType, ThumbnailMaxBytes, cancellationToken);
        }

        return Result<PresignAttachmentResult>.Success(
            new PresignAttachmentResult(uploadId, image, thumbnail));
    }

    private async Task<PresignedTarget> SignAsync(
        string key, string contentType, long maxBytes, CancellationToken ct)
    {
        var upload = await _storage.GeneratePresignedPostAsync(
            bucket: _buckets.Attachments,
            objectKey: key,
            constraints: new UploadConstraints(contentType, MinBytes: 1, MaxBytes: maxBytes),
            expiresIn: UploadTtl,
            ct: ct);

        return new PresignedTarget(upload.Url, upload.Fields, upload.ObjectKey, upload.ExpiresAt);
    }
}
