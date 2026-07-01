using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Phase S.8c — persistence for admin-issued long-lived system JWTs.
/// Row is inserted on Issue, updated on Revoke, listed in the admin UI.
/// See <see cref="SystemIssuedToken"/> for the "why we don't store the
/// JWT body" note.
/// </summary>
public interface ISystemIssuedTokenRepository
{
    Task AddAsync(SystemIssuedToken token, CancellationToken ct = default);

    /// <summary>List all tokens issued for a system, newest first.
    /// Includes both Active and Revoked so the admin can see the full
    /// audit trail without a separate query.</summary>
    Task<IReadOnlyList<SystemIssuedToken>> ListBySystemAsync(string systemKey, CancellationToken ct = default);

    /// <summary>Lookup by JTI — used by the revoke endpoint to find the
    /// row before marking it revoked (also validates that the JTI
    /// belongs to a system the caller is allowed to manage).</summary>
    Task<SystemIssuedToken?> GetByJtiAsync(string jti, CancellationToken ct = default);

    /// <summary>Persist the domain <see cref="SystemIssuedToken.Revoke"/>
    /// mutation. Repository owns the SaveChanges call so the endpoint
    /// stays declarative.</summary>
    Task UpdateAsync(SystemIssuedToken token, CancellationToken ct = default);
}
