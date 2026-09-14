namespace DTMS.Fleet.Domain.Entities;

/// <summary>
/// What a list row needs to know about one owner's images: which one to show,
/// and how many there are. Carries an id rather than a URL — a list never signs
/// anything, and a stable id is what lets the browser cache the picture.
/// </summary>
public readonly record struct AttachmentSummary(Guid CoverId, int Count)
{
    /// <summary>
    /// The cover is the newest image, the same one the gallery shows first.
    ///
    /// <para>Ties on <c>UploadedAt</c> fall to the larger <c>Id</c>, matching the
    /// gallery's own <c>ORDER BY "UploadedAt" DESC, "Id" DESC</c>. Without a
    /// tiebreaker two images stamped in the same instant could swap places
    /// between requests, and the thumbnail in a row would stop agreeing with the
    /// first photo in its gallery. .NET orders a Guid the way PostgreSQL orders a
    /// uuid, so the two sides pick the same one.</para>
    ///
    /// <para>Lives here, as a plain function, because the rule is the part worth
    /// testing and the repository that feeds it cannot be exercised without a
    /// database.</para>
    /// </summary>
    public static IReadOnlyDictionary<Guid, AttachmentSummary> Summarize(
        IEnumerable<(Guid OwnerId, Guid Id, DateTime UploadedAt)> images)
    {
        var result = new Dictionary<Guid, AttachmentSummary>();
        var covers = new Dictionary<Guid, (Guid Id, DateTime UploadedAt)>();

        foreach (var (ownerId, id, uploadedAt) in images)
        {
            if (!covers.TryGetValue(ownerId, out var cover) || IsNewer(id, uploadedAt, cover))
                covers[ownerId] = (id, uploadedAt);

            result[ownerId] = new AttachmentSummary(
                covers[ownerId].Id,
                result.TryGetValue(ownerId, out var seen) ? seen.Count + 1 : 1);
        }

        return result;
    }

    private static bool IsNewer(Guid id, DateTime uploadedAt, (Guid Id, DateTime UploadedAt) current)
        => uploadedAt > current.UploadedAt
           || (uploadedAt == current.UploadedAt && id.CompareTo(current.Id) > 0);
}
