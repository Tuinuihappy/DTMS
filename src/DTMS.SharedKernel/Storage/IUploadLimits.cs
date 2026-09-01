namespace DTMS.SharedKernel.Storage;

/// <summary>
/// Ceilings baked into every upload policy, resolved from configuration at the
/// composition root. Same indirection as <see cref="IStorageBuckets"/>: it lets
/// Application layers build a policy without taking an options dependency.
/// </summary>
public interface IUploadLimits
{
    long MaxUploadBytes { get; }

    /// <summary>
    /// Exact content types permitted, <b>not</b> a prefix.
    ///
    /// <para>An "image/" prefix would admit <c>image/svg+xml</c>, and an SVG
    /// served under its own type executes script on the storage origin. The
    /// allowed type is pinned into the upload policy as an exact match, so the
    /// uploader cannot substitute another one after the URL is issued.</para>
    /// </summary>
    IReadOnlyList<string> AllowedContentTypes { get; }

    int MaxAttachmentsPerOwner { get; }

    bool IsAllowed(string? contentType) =>
        contentType is not null &&
        AllowedContentTypes.Any(t => string.Equals(t, contentType, StringComparison.OrdinalIgnoreCase));
}
