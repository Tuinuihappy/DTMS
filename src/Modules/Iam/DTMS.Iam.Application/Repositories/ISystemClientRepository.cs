using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Read-side access to <see cref="SystemClient"/> + the principal's
/// permission set. Admin CRUD lands in a separate write-side
/// repository (Phase S.4); this interface covers the hot paths that
/// the auth middleware + permission transformer hit per request, so
/// it stays narrow.
/// </summary>
public interface ISystemClientRepository
{
    Task<SystemClient?> GetByKeyAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Active systems only — used by the master outbox processor to
    /// reconcile worker tasks against the DB on each discovery cycle.
    /// </summary>
    Task<IReadOnlyList<SystemClient>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Permission codes granted directly to this system. Wildcards
    /// (<c>dtms:*</c>, <c>dtms:source:*</c>) are returned verbatim;
    /// the claims transformer expands them at enforcement time.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionCodesAsync(string systemKey, CancellationToken ct = default);
}
