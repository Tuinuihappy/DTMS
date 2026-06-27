using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Append-only audit log of IAM admin actions. Reads are paginated +
/// filterable; writes never update or delete (regulatory rule of thumb).
/// </summary>
public interface IAuditLogRepository
{
    Task AppendAsync(PermissionAuditEntry entry, CancellationToken ct = default);

    Task<(IReadOnlyList<PermissionAuditEntry> Items, int TotalCount)> QueryAsync(
        string? actorEmployeeId,
        string? role,
        string? action,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
