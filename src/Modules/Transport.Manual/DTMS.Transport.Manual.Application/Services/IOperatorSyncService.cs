using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Services;

// Phase 4.2 — Per-request sync of the Operator row from JWT claims
// (per ADR-014). The middleware in the API layer calls this on every
// authenticated request; idempotent and cheap (one indexed lookup + an
// UPDATE only when claims drift).
public interface IOperatorSyncService
{
    Task<Operator> SyncFromClaimsAsync(
        string employeeCode,
        string displayName,
        OperatorRole role,
        string? thumbnailUrl,
        Guid? primaryWarehouseId,
        CancellationToken ct = default);
}
