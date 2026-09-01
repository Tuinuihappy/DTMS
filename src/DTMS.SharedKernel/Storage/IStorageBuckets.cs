namespace DTMS.SharedKernel.Storage;

/// <summary>
/// Bucket names, resolved from configuration at the composition root.
///
/// <para>The indirection exists so Application layers can name a bucket without
/// referencing <c>IOptions&lt;&gt;</c> — the same reason the POD-only version of
/// this interface existed before it was generalised. Adding an options package
/// to SharedKernel to save one small interface would be the worse trade.</para>
/// </summary>
public interface IStorageBuckets
{
    /// <summary>Proof-of-delivery photos (ADR-015). Separate from attachments
    /// because it already holds live objects; there is nothing to gain by
    /// moving them.</summary>
    string Pod { get; }

    /// <summary>Carrier / carrier-type / maintenance images (ADR-019).</summary>
    string Attachments { get; }
}
