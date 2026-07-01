using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Persistence access for <see cref="SystemClient"/> + the principal's
/// permission set. Hot-path readers (auth middleware, claims
/// transformer) hit <see cref="GetByKeyAsync"/> +
/// <see cref="GetPermissionCodesAsync"/>; admin CRUD (Phase S.4)
/// adds the write methods alongside the read ones — one interface per
/// aggregate keeps DI flat and avoids two-context fan-out under one
/// admin request.
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

    // ── Phase S.4 admin CRUD ────────────────────────────────────────────

    /// <summary>All systems including deactivated. Admin list view.</summary>
    Task<IReadOnlyList<SystemClient>> ListAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Persist a new <see cref="SystemClient"/> + its auto-seeded
    /// standard permissions atomically. The caller passes the
    /// permission codes (resolved from
    /// <see cref="DTMS.Iam.Application.Authorization.StandardSystemPermissions"/>)
    /// — keeping the substitution at the call site so the repo doesn't
    /// import the Authorization namespace.
    /// </summary>
    Task AddWithPermissionsAsync(SystemClient client, IReadOnlyList<string> permissionCodes, string grantedBy, CancellationToken ct = default);

    /// <summary>
    /// Replace the tracked entity with the in-memory state — used after
    /// metadata edits or Activate/Deactivate via the domain methods.
    /// </summary>
    Task UpdateAsync(SystemClient client, CancellationToken ct = default);

    /// <summary>
    /// Hard delete. FK cascade in the Iam schema removes SystemCredential,
    /// SystemClientPermissions, and SystemEventSubscriptions rows that
    /// reference this Key. Cross-module data (DeliveryOrders.SourceSystem,
    /// outbox.OutboxMessages.PartitionKey, audit log entries) carry the
    /// key as a denormalized string — by design they have no FK and are
    /// preserved as historical fact when the SystemClient is removed.
    /// </summary>
    Task RemoveAsync(SystemClient client, CancellationToken ct = default);

    /// <summary>
    /// Insert (<paramref name="systemKey"/>, <paramref name="permissionCode"/>)
    /// into <c>iam.SystemClientPermissions</c>. Returns <c>true</c> if a
    /// row was inserted, <c>false</c> if the pair already exists
    /// (idempotent — multi-tab clicks both resolve to "already granted").
    /// </summary>
    Task<bool> GrantPermissionAsync(string systemKey, string permissionCode, string grantedBy, CancellationToken ct = default);

    /// <summary>
    /// Delete (<paramref name="systemKey"/>, <paramref name="permissionCode"/>)
    /// from <c>iam.SystemClientPermissions</c>. Returns <c>true</c> if a
    /// row was removed, <c>false</c> if the pair was not granted.
    /// </summary>
    Task<bool> RevokePermissionAsync(string systemKey, string permissionCode, CancellationToken ct = default);
}
