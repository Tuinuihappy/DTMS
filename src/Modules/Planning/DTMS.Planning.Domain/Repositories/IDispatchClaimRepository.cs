using DTMS.Planning.Domain.Entities;

namespace DTMS.Planning.Domain.Repositories;

public interface IDispatchClaimRepository
{
    /// <summary>
    /// Atomically claim <paramref name="idempotencyKey"/> by inserting a row.
    /// The unique index does the arbitration, so exactly one concurrent caller
    /// wins — this is the de-dup guard and it MUST be committed before the
    /// vendor call, so a crash mid-dispatch still leaves evidence.
    /// </summary>
    /// <returns>
    /// The new claim when this caller won; <c>null</c> when the key was
    /// already taken (the caller should then load the existing claim and
    /// decide replay / reject / retry).
    /// </returns>
    Task<DispatchClaim?> TryClaimAsync(
        string idempotencyKey,
        Guid orderTemplateId,
        string requestHash,
        CancellationToken cancellationToken = default);

    Task<DispatchClaim?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Most recent attempt for a template — drives the "last dispatch" hint in the UI.</summary>
    Task<DispatchClaim?> GetLatestByTemplateAsync(Guid orderTemplateId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
