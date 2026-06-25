namespace AMR.DeliveryPlanning.Transport.Manual.Application.Services;

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
    public static bool BelongsToTripLeg(string objectKey, Guid tripId, string kind)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return false;
        var prefix = $"pod/{tripId}/{kind}/";
        return objectKey.StartsWith(prefix, StringComparison.Ordinal);
    }
}
