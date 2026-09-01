using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;
using DTMS.SharedKernel.Storage;
using Microsoft.Extensions.Logging;

namespace DTMS.Fleet.Application.Commands.ConfirmAttachment;

/// <summary>
/// Turns a completed upload into a record.
///
/// <para>The bytes went straight from the browser to storage, so this is the
/// first moment the server can know the upload happened at all — and it finds
/// out by asking storage, not by believing the caller. Size and content type
/// are read back from the object; the request supplies only what storage cannot
/// know, which is the original file name and the caption.</para>
/// </summary>
public record ConfirmAttachmentCommand(
    AttachmentOwner Owner,
    Guid OwnerId,
    Guid UploadId,
    bool WithThumbnail,
    string? OriginalFileName = null,
    string? Caption = null) : ICommand<Guid>;

internal sealed class ConfirmAttachmentCommandHandler : ICommandHandler<ConfirmAttachmentCommand, Guid>
{
    private readonly IObjectStorageService _storage;
    private readonly IStorageBuckets _buckets;
    private readonly IUploadLimits _limits;
    private readonly IAttachmentOwnerLookup _owners;
    private readonly IAttachmentRepository _attachments;
    private readonly ICurrentActorContext _actor;
    private readonly ILogger<ConfirmAttachmentCommandHandler> _logger;

    public ConfirmAttachmentCommandHandler(
        IObjectStorageService storage,
        IStorageBuckets buckets,
        IUploadLimits limits,
        IAttachmentOwnerLookup owners,
        IAttachmentRepository attachments,
        ICurrentActorContext actor,
        ILogger<ConfirmAttachmentCommandHandler> logger)
    {
        _storage = storage;
        _buckets = buckets;
        _limits = limits;
        _owners = owners;
        _attachments = attachments;
        _actor = actor;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(
        ConfirmAttachmentCommand request, CancellationToken cancellationToken)
    {
        if (!await _owners.ExistsAsync(request.Owner, request.OwnerId, cancellationToken))
            return Result<Guid>.Failure($"{request.Owner} '{request.OwnerId}' not found.");

        var bucket = _buckets.Attachments;
        var stagingKey = AttachmentObjectKey.Staging(request.UploadId);

        var stat = await _storage.StatAsync(bucket, stagingKey, cancellationToken);
        if (!stat.Exists)
            return Result<Guid>.Failure(
                "The upload did not complete. Try again — nothing was saved.");

        // Read back rather than taken from the request. Size is measured by the
        // storage server. Content type is the value the upload policy pinned,
        // which the client could not have altered without invalidating the
        // signature — so this is the server's own value coming home, and the
        // allow-list check is belt and braces against an object that arrived by
        // some other route.
        if (!_limits.IsAllowed(stat.ContentType))
        {
            _logger.LogWarning(
                "Attachment confirm rejected: {Bucket}/{Key} has content type {ContentType}, which is not allowed.",
                bucket, stagingKey, stat.ContentType);
            return Result<Guid>.Failure("That file is not an accepted image type.");
        }

        // Re-checked because presign and confirm are separate requests and the
        // count can move between them. Two confirms racing can still both pass:
        // "no more than N" is not expressible as a database constraint without
        // a trigger, and the worst outcome is one extra image — not worth one.
        var existing = await _attachments.CountForOwnerAsync(
            request.Owner, request.OwnerId, cancellationToken);
        if (existing >= _limits.MaxAttachmentsPerOwner)
            return Result<Guid>.Failure(
                $"This already has {existing} images, the maximum is {_limits.MaxAttachmentsPerOwner}.");

        var extension = AttachmentObjectKey.ExtensionFor(stat.ContentType);
        var finalKey = AttachmentObjectKey.Final(
            request.Owner, request.OwnerId, request.UploadId, extension);

        string? finalThumbKey = null;
        var stagingThumbKey = AttachmentObjectKey.StagingThumbnail(request.UploadId);
        if (request.WithThumbnail &&
            (await _storage.StatAsync(bucket, stagingThumbKey, cancellationToken)).Exists)
        {
            finalThumbKey = AttachmentObjectKey.FinalThumbnail(
                request.Owner, request.OwnerId, request.UploadId, extension);
        }

        // Promote before writing the row, so the row never points at a key that
        // does not exist yet. A copy is server-side — the bytes do not travel
        // through this process. If it throws, nothing is written and the
        // staging objects expire on their own.
        await _storage.CopyAsync(bucket, stagingKey, finalKey, cancellationToken);
        if (finalThumbKey is not null)
            await _storage.CopyAsync(bucket, stagingThumbKey, finalThumbKey, cancellationToken);

        var attachment = Attachment.For(
            owner: request.Owner,
            ownerId: request.OwnerId,
            bucket: bucket,
            objectKey: finalKey,
            thumbnailKey: finalThumbKey,
            contentType: stat.ContentType,
            sizeBytes: stat.SizeBytes,
            originalFileName: request.OriginalFileName,
            caption: request.Caption,
            uploadedBy: ActorName.Of(_actor));

        await _attachments.AddAsync(attachment, cancellationToken);
        await _attachments.SaveChangesAsync(cancellationToken);

        // Best effort, after the row is safely committed. Failing here costs a
        // duplicate that the staging lifecycle rule removes anyway, so it must
        // never turn a successful confirm into an error.
        await TryDeleteStagingAsync(bucket, stagingKey, cancellationToken);
        if (request.WithThumbnail)
            await TryDeleteStagingAsync(bucket, stagingThumbKey, cancellationToken);

        return Result<Guid>.Success(attachment.Id);
    }

    private async Task TryDeleteStagingAsync(string bucket, string key, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(bucket, key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Could not clear staging object {Bucket}/{Key}; the lifecycle rule will expire it.",
                bucket, key);
        }
    }
}
