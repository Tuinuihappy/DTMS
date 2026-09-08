namespace DTMS.Transport.Manual.Application.Services;

// Phase 4.3 — Canonical object-key naming for POD uploads. Layout:
//   pod/{tripId}/{kind}/{ulid}.{ext}
//
// Why this shape:
//   - tripId prefix lets ops `mc ls pod/<tripId>` to find every POD for
//     a single delivery (pickup + drop + any retries).
//   - 'kind' segment ('pickup' / 'drop') makes purpose obvious from the
//     key alone — useful when stamping the wrong key onto the wrong leg
//     would otherwise be silent.
//   - Random suffix prevents accidental overwrites (operator retries
//     upload after a network blip would clobber the prior good copy if
//     we re-used the same key).
//
// Validation rule: any key DTMS accepts on RecordPickup/RecordDrop MUST
// match the (tripId, kind) the operator is actually claiming for.
// Stops a compromised operator session from referencing another trip's
// photo by re-using its key.
public static class PodObjectKey
{
    public const string KindPickup = "pickup";
    public const string KindDrop = "drop";

    /// <summary>
    /// Where an upload lands before the leg that references it is recorded.
    ///
    /// <para>POD used to be written straight to its final key, which made an
    /// abandoned capture — a retaken photo, a closed app, a leg never confirmed —
    /// indistinguishable from a real one without consulting the database, so
    /// every one of them stayed. Staging makes the difference readable from the
    /// key alone, which is what lets a bucket lifecycle rule collect the garbage
    /// and keeps the authority to delete a real proof of delivery out of our
    /// code.</para>
    /// </summary>
    public const string IncomingPrefix = "incoming/";

    /// <summary>The key an upload is presigned against. Same tail as the final
    /// key, so promoting is removing a prefix and nothing else can drift.</summary>
    public static string GenerateStaging(Guid tripId, string kind, string fileExtension = "jpg")
        => IncomingPrefix + Generate(tripId, kind, fileExtension);

    public static bool IsStaged(string objectKey) =>
        objectKey.StartsWith(IncomingPrefix, StringComparison.Ordinal);

    /// <summary>The resting place for a staged key. Returns null for a key that
    /// was not staged, which the caller treats as "already final".</summary>
    public static string? PromoteToFinal(string objectKey) =>
        IsStaged(objectKey) ? objectKey[IncomingPrefix.Length..] : null;

    public static string Generate(Guid tripId, string kind, string fileExtension = "jpg")
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Kind must be non-empty.", nameof(kind));
        if (kind != KindPickup && kind != KindDrop)
            throw new ArgumentException($"Kind must be '{KindPickup}' or '{KindDrop}'.", nameof(kind));

        var ext = string.IsNullOrWhiteSpace(fileExtension) ? "bin" : fileExtension.TrimStart('.');
        return $"pod/{tripId}/{kind}/{Guid.NewGuid():N}.{ext}";
    }

    // Cheap server-side guard against operator app passing a key for
    // someone else's trip. Real auth + ACL still belong on the bucket
    // policy; this is the defence-in-depth check on the .NET side.
    //
    // Accepts a key in either stage: the leg being recorded is the moment a
    // staged upload is promoted, so both forms legitimately arrive here.
    public static bool BelongsToTripLeg(string objectKey, Guid tripId, string kind)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return false;
        var tail = PromoteToFinal(objectKey) ?? objectKey;
        return tail.StartsWith($"pod/{tripId}/{kind}/", StringComparison.Ordinal);
    }
}
