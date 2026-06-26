using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Services;

public sealed class OperatorSyncService : IOperatorSyncService
{
    private readonly IOperatorRepository _repo;
    public OperatorSyncService(IOperatorRepository repo) => _repo = repo;

    public async Task<Operator> SyncFromClaimsAsync(
        string employeeCode,
        string displayName,
        OperatorRole role,
        string? thumbnailUrl,
        Guid? primaryWarehouseId,
        CancellationToken ct = default)
    {
        var existing = await _repo.GetByEmployeeCodeAsync(employeeCode, ct);
        if (existing is null)
        {
            // First-time login — create the DTMS-side row. Domain
            // factory raises OperatorRegisteredDomainEvent.
            var created = Operator.CreateFromJwtClaims(
                employeeCode, displayName, role,
                primaryWarehouseId: primaryWarehouseId,
                phone: null,
                thumbnailUrl: thumbnailUrl);
            await _repo.AddAsync(created, ct);
            await _repo.SaveChangesAsync(ct);
            return created;
        }

        // Subsequent logins — overwrite DisplayName + Role (External
        // Auth owns those). PrimaryWarehouseId from claims is only
        // applied when DTMS doesn't already have one — dispatcher
        // console may override locally and we don't want to clobber.
        var nameDrifted = existing.DisplayName != displayName.Trim();
        var roleDrifted = existing.Role != role;
        if (nameDrifted || roleDrifted)
        {
            existing.SyncFromJwtClaims(displayName, role, thumbnailUrl);
        }
        if (primaryWarehouseId.HasValue && existing.PrimaryWarehouseId is null)
        {
            existing.SetPrimaryWarehouse(primaryWarehouseId);
        }
        if (nameDrifted || roleDrifted || (primaryWarehouseId.HasValue && existing.PrimaryWarehouseId == primaryWarehouseId))
        {
            await _repo.SaveChangesAsync(ct);
        }
        return existing;
    }
}
