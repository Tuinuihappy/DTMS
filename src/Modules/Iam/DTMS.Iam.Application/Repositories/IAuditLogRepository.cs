using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Append-only audit log of IAM admin actions. Reads are paginated +
/// filterable; writes never update or delete (regulatory rule of thumb).
/// </summary>
public interface IAuditLogRepository
{
    Task AppendAsync(PermissionAuditEntry entry, CancellationToken ct = default);

    /// <param name="systemKey">
    /// Rows about one system client (oms, wms, …). Every system-* and
    /// subscription-* row carries it as <c>"systemKey"</c> in Details.
    /// </param>
    Task<(IReadOnlyList<PermissionAuditEntry> Items, int TotalCount)> QueryAsync(
        string? actorEmployeeId,
        string? role,
        string? action,
        int page,
        int pageSize,
        string? systemKey = null,
        CancellationToken ct = default);

    /// <summary>
    /// Every action that occurs in the log, so the admin filter lists what
    /// is really there instead of a hand-kept list that drifts.
    /// </summary>
    Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct = default);
}
