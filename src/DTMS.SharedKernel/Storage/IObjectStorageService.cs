namespace DTMS.SharedKernel.Storage;

/// <summary>
/// Object storage, addressed by bucket + key. Lives in SharedKernel because
/// more than one module needs it — POD photos (ADR-015) and carrier/maintenance
/// attachments (ADR-019) — and <c>ModuleBoundaryTests</c> requires modules to
/// depend on abstractions here rather than on each other's infrastructure.
///
/// <para>The concrete MinIO implementation is wired at the composition root.
/// Nothing in this file may know that MinIO exists.</para>
/// </summary>
public interface IObjectStorageService
{
    /// <summary>
    /// A signed form post the browser can upload directly to, so photo bytes
    /// never pass through the API.
    ///
    /// <para>Unlike a presigned PUT, the returned policy carries conditions the
    /// storage server itself enforces — size range, exact content type, exact
    /// key — so an oversized or wrong-typed upload is refused before any byte
    /// is written. A PUT URL can express none of that: it authorises "write
    /// anything, any size, to this key".</para>
    /// </summary>
    Task<PresignedUpload> GeneratePresignedPostAsync(
        string bucket,
        string objectKey,
        UploadConstraints constraints,
        TimeSpan expiresIn,
        CancellationToken ct = default);

    /// <summary>
    /// The older, weaker upload: a signed URL that authorises writing anything
    /// of any size to one key.
    ///
    /// <para>Retained only so relocating this contract into SharedKernel is a
    /// pure move with no behavioural change. The POD capture flow is switched
    /// to <see cref="GeneratePresignedPostAsync"/> in the next step and this
    /// member goes with it — do not build anything new on it.</para>
    /// </summary>
    Task<PresignedPutUrl> GeneratePresignedPutAsync(
        string bucket,
        string objectKey,
        TimeSpan expiresIn,
        string? contentType = null,
        CancellationToken ct = default);

    /// <summary>
    /// A time-limited URL for reading one object, for an <c>&lt;img src&gt;</c>.
    ///
    /// <para><paramref name="responseContentType"/> overrides the type the
    /// object was stored with. Pass the value validated at upload time: the
    /// stored type is whatever the uploader claimed, so serving it back
    /// unchecked would let a file uploaded as <c>image/svg+xml</c> execute
    /// script on the storage origin.</para>
    ///
    /// <para>The URL is a bearer capability for its lifetime — anyone holding
    /// it can read the object until it expires.</para>
    /// </summary>
    Task<string> GeneratePresignedGetAsync(
        string bucket,
        string objectKey,
        TimeSpan expiresIn,
        string? responseContentType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Server-side copy — the bytes move inside the storage server and never
    /// travel through this process. Used to promote a confirmed upload out of
    /// the staging prefix into its final key.
    /// </summary>
    Task CopyAsync(
        string bucket,
        string sourceKey,
        string destinationKey,
        CancellationToken ct = default);

    /// <summary>
    /// Removes an object. <b>Deleting something that is already gone counts as
    /// success</b> — callers are retried delete instructions from the outbox,
    /// and treating the second attempt as a failure would park the row in the
    /// dead-letter queue forever.
    /// </summary>
    Task DeleteAsync(string bucket, string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Existence check. Kept as its own method (rather than folded into
    /// <see cref="StatAsync"/>) because the manual pickup/drop handlers ask
    /// only this question and should not be rewritten to unpack metadata they
    /// do not use.
    /// </summary>
    Task<bool> ObjectExistsAsync(string bucket, string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Metadata for an uploaded object.
    ///
    /// <para><b>Only <c>SizeBytes</c> is trustworthy.</b> The storage server
    /// measures it. <c>ContentType</c> is whatever the uploader sent and is
    /// echoed back unchanged, so it is a claim, not a fact — the upload policy
    /// is what constrains it.</para>
    /// </summary>
    Task<ObjectMetadata> StatAsync(string bucket, string objectKey, CancellationToken ct = default);

    /// <summary>Idempotent; safe to call on every boot.</summary>
    Task EnsureBucketExistsAsync(string bucket, CancellationToken ct = default);
}

/// <summary>Conditions baked into an upload policy and enforced by the storage server.</summary>
public sealed record UploadConstraints(
    string ContentType,
    long MinBytes,
    long MaxBytes);

/// <summary>
/// A signed form post. <paramref name="Fields"/> must be written into the
/// multipart body <b>before</b> the file part — S3-compatible servers read the
/// policy from the leading fields and reject a body that puts the file first.
/// </summary>
public sealed record PresignedUpload(
    string Url,
    IReadOnlyDictionary<string, string> Fields,
    string ObjectKey,
    DateTime ExpiresAt);

/// <summary>Transitional — see <see cref="IObjectStorageService.GeneratePresignedPutAsync"/>.</summary>
public sealed record PresignedPutUrl(
    string UploadUrl,
    string ObjectKey,
    DateTime ExpiresAt);

// Named ObjectMetadata rather than ObjectStat so it does not collide with the
// MinIO SDK type of that name inside the implementation.
public sealed record ObjectMetadata(bool Exists, long SizeBytes, string ContentType)
{
    public static readonly ObjectMetadata Missing = new(false, 0, string.Empty);
}
