using DTMS.Fleet.Domain.Entities;

namespace DTMS.Fleet.Application.Services;

/// <summary>
/// Where an attachment's bytes live, across the two stages of its life.
///
/// <para>Uploads land under <see cref="IncomingPrefix"/> and are promoted to a
/// final key only once confirmed. That split is what makes abandoned uploads —
/// closed tab, lost signal, changed mind — identifiable without consulting the
/// database: anything left in the staging prefix is garbage by definition, so a
/// storage lifecycle rule can expire it and no code needs the authority to
/// delete real objects. POD went the other way at first — straight to a final
/// key — and accumulated exactly the abandoned uploads this prevents, until it
/// adopted the same scheme (see <c>PodObjectKey.IncomingPrefix</c>).</para>
/// </summary>
public static class AttachmentObjectKey
{
    public const string IncomingPrefix = "incoming/";

    /// <summary>Marks a thumbnail so the pair is obvious when listing a bucket.</summary>
    private const string ThumbnailMarker = ".thumb";

    public static string Staging(Guid uploadId) => $"{IncomingPrefix}{uploadId:N}";

    public static string StagingThumbnail(Guid uploadId) => Staging(uploadId) + ThumbnailMarker;

    /// <summary>
    /// Final resting place, prefixed by owner so one owner's images can be
    /// listed directly. Mirrors the POD layout (<c>pod/{tripId}/{kind}/…</c>).
    /// </summary>
    public static string Final(AttachmentOwner owner, Guid ownerId, Guid uploadId, string extension)
        => $"{Prefix(owner)}/{ownerId}/{uploadId:N}{Normalize(extension)}";

    public static string FinalThumbnail(AttachmentOwner owner, Guid ownerId, Guid uploadId, string extension)
        => $"{Prefix(owner)}/{ownerId}/{uploadId:N}{ThumbnailMarker}{Normalize(extension)}";

    public static string Prefix(AttachmentOwner owner) => owner switch
    {
        AttachmentOwner.Carrier => "carrier",
        AttachmentOwner.CarrierType => "carrier-type",
        AttachmentOwner.MaintenanceLog => "maintenance",
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown attachment owner.")
    };

    /// <summary>Derives the extension from the pinned content type rather than
    /// the uploaded file name, which is attacker-supplied and lands in a URL.</summary>
    public static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin"
    };

    private static string Normalize(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? string.Empty
        : extension.StartsWith('.') ? extension
        : "." + extension;
}
